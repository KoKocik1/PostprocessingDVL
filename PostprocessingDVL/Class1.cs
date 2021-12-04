using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using PartyMaker;
namespace PostprocessingDVL
{
    class Class1
    {

            ConfigPostprocessing config = YamlRead.ReadConfigFile();

            //Odczyt danych
            MySqlConnection conn;

            //public DateTime (int year, int month, int day, int hour, int minute, int second, int millisecond);
            public List<DateTime> poczatekLista = new List<DateTime>();
            public List<DateTime> koniecLista = new List<DateTime>();

            // kurs ahrs czy sat
            bool liczSatelitarnie_flag;

            static void Main(string[] args)
            {
            Class1 program = new Class1();
            }

            public Class1()
            {
                //polaczenie z baza
                DBConnect connDB = new DBConnect();
                conn = connDB.OpenConnection();


                if (config.ZrodloKursu == "sat")
                    liczSatelitarnie_flag = true;
                else
                    liczSatelitarnie_flag = false;

                Calkowanie.LiczLocal_flag = config.LiczLocalTime;
                FindElementsByTimePostProcessing.LiczLocal_flag = config.LiczLocalTime;

                foreach (var czas in config.PrzedzialyCzasowe)
                    poczatekLista.Add(czas.Poczatek);
                foreach (var czas in config.PrzedzialyCzasowe)
                    koniecLista.Add(czas.Koniec);

                //obliczenie wszystkich przedziałów i zapisanie do plików
                for (int przedzial = 0; przedzial < poczatekLista.Count; przedzial++)
                {
                    try
                    {
                        DateTime poczatek = poczatekLista.ElementAt(przedzial);
                        DateTime koniec = koniecLista.ElementAt(przedzial);

                        //Wprowadzenie poprawki czasowej dla dvl
                        TimeSpan poprawkaDVL = new TimeSpan(0, 0, 0, config.PoprawkaDVLsecond, config.PoprawkaDVLmiliseconds);
                        DateTime poczatekDVL = poczatek.Subtract(poprawkaDVL);
                        DateTime koniecDVL = koniec.Subtract(poprawkaDVL);

                        //******************************************************************************

                        List<gps> odczytyGPS_B = ReadDB.readGPS(poczatek, koniec, "B", conn);
                        List<dvl_bottom> odczytyDVL_bottom = ReadDB.readDVLbottom(poczatekDVL, koniecDVL, conn);
                        List<dvl_water> odczytyDVL_water = ReadDB.readDVLwater(poczatekDVL, koniecDVL, conn);
                        List<initbark> geometricalPoints = config.Initbarks;
                        List<quat_ahrs> odczytyAhrs = ReadDB.readAhrs(poczatek, koniec, conn);
                        //******************************************************************************

                        //Wprowadzenie poprawki na osie DVL oraz na czas DVL

                        //foreach (dvl_bottom odczyt in odczytyDVL_bottom)
                        //{
                        //    odczyt.time = ((DateTime)(odczyt.time)).Add(poprawkaDVL);
                        //    odczyt.vx = -odczyt.vx;
                        //    odczyt.vy = -odczyt.vy;
                        //}
                        //foreach (dvl_water odczyt in odczytyDVL_water)
                        //{
                        //    odczyt.time = ((DateTime)(odczyt.time)).Add(poprawkaDVL);
                        //    odczyt.vx = -odczyt.vx;
                        //    odczyt.vy = -odczyt.vy;
                        //}

                        //******************************************************************************
                        //Określenie zależności geometrycznych
                        //Przeliczenie CRP do bazowego GPS
                        initbark baseReferencePoint = null;
                        initbark dvlPoint = null;
                        List<initbark> pointsForRotation = new List<initbark>();
                        foreach (initbark point in geometricalPoints)
                        {
                            if (point.Identyfikator.Equals("B"))
                                baseReferencePoint = point;
                            if (point.Rola.Equals("Punkt"))
                                pointsForRotation.Add(point);
                            if (point.Identyfikator.Equals("D"))
                                dvlPoint = point;
                        }

                        double geometricCorrection = 0;
                        RotElements dvl = null;
                        if (baseReferencePoint != null)
                        {
                            //Obliczenie pozycji DVL wzgledem bazy GPS
                            if (dvlPoint != null)
                            {
                                dvl = new RotElements(dvlPoint.Identyfikator, (double)(dvlPoint.XCoordinate - baseReferencePoint.ZCoordinate),
                                             (double)(dvlPoint.YCoordinate - baseReferencePoint.YCoordinate), (double)(dvlPoint.ZCoordinate - baseReferencePoint.ZCoordinate));
                            }
                            else
                                Console.Out.WriteLine("Brak konfiguracji kompasu satelitarnego. Sprawdź bazę danych - tablica initbark");
                        }

                        //******************************************************************************
                        ////Właściwe obliczenia

                        bool start_flag = false;
                        bool inicjalizacja_flag = false;

                        dvl_position biezacyPozycja_bottom = new dvl_position();
                        dvl_position_water biezacyPozycja_water = new dvl_position_water();

                        gps biezacyGPS_B = null;
                        double biezacyKurs = 0;

                        List<dvl_position> wyniki_bottom = new List<dvl_position>();
                        List<dvl_position_water> wyniki_water = new List<dvl_position_water>();
                        List<double> wyniki_kurs = new List<double>();


                        //Pomiary względem dna
                        foreach (dvl_bottom odczyt in odczytyDVL_bottom)
                        {
                            if (odczyt.vx > -30 && odczyt.vy > -30 && odczyt.vz > -30 && odczyt.vx < 30 && odczyt.vy < 30 && odczyt.vz < 30)
                            {
                                if (config.LiczLocalTime)
                                {
                                    if (odczytyGPS_B.Count != 0)
                                    {
                                        biezacyGPS_B = FindElementsByTimePostProcessing.szukajPozycjiGPS((DateTime)odczyt.local_time, odczytyGPS_B);
                                    }
                                    else
                                        biezacyGPS_B = null;
                                }
                                else
                                {
                                    DateTime dataDvl = (DateTime)odczyt.device_time;
                                    //dataDvl = dataDvl.AddSeconds(14);
                                    //dataDvl = dataDvl.AddMilliseconds(500);

                                    if (odczytyGPS_B.Count != 0)
                                    {
                                        biezacyGPS_B = FindElementsByTimePostProcessing.szukajPozycjiGPS(dataDvl, odczytyGPS_B);
                                    }
                                    else
                                        biezacyGPS_B = null;

                                }

                                if ( biezacyGPS_B != null)
                                {

                                    
                                        quat_ahrs najblizszy = null;
                                        if (odczytyAhrs.Count != 0)
                                        {
                                            najblizszy = FindElementsByTimePostProcessing.szukajPozycjiAhrs((DateTime)odczyt.local_time, odczytyAhrs);
                                            biezacyKurs = (double)najblizszy.yaw;

                                            //potrzebne?
                                            if (biezacyKurs < -180)
                                                biezacyKurs += 360;
                                            if (biezacyKurs > 180)
                                                biezacyKurs -= 360;

                                            biezacyKurs -= config.KursAhrsPoprawka;
                                            biezacyPozycja_bottom = Calkowanie.integrationDvlBottom(Convert.ToSingle(biezacyKurs),
                                                        Convert.ToSingle(najblizszy.pitch), Convert.ToSingle(najblizszy.roll), biezacyPozycja_bottom, odczyt);
                                        }
                                        else
                                        {
                                            biezacyKurs = 0;
                                            biezacyPozycja_bottom = null;
                                        }
                                    
                                }
                                else
                                {
                                    biezacyKurs = 0;
                                    biezacyPozycja_bottom = null;
                                }


                                if (!inicjalizacja_flag)
                                    start_flag = true;
                                else
                                {
                                    biezacyPozycja_bottom.lat0 = dvl.lat;
                                    biezacyPozycja_bottom.lon0 = dvl.lon;
                                    wyniki_bottom.Add(biezacyPozycja_bottom);
                                    wyniki_kurs.Add(biezacyKurs);
                                }
                            }
                            if (start_flag)
                            {
                                //Inicjalizacja wartości początkowych
                                dvl.rotForPosition(biezacyKurs, (double)biezacyGPS_B.lat, (double)biezacyGPS_B.lon);
                                biezacyPozycja_bottom.lat = dvl.lat;
                                biezacyPozycja_bottom.lon = dvl.lon;
                                biezacyPozycja_bottom.lat0 = dvl.lat;
                                biezacyPozycja_bottom.lon0 = dvl.lon;
                                wyniki_bottom.Add(biezacyPozycja_bottom);
                                wyniki_kurs.Add(biezacyKurs);
                                start_flag = false;
                                inicjalizacja_flag = true;
                            }

                        }
                        Console.Out.WriteLine(biezacyPozycja_bottom.lat);
                        Console.Out.WriteLine(biezacyPozycja_bottom.lon);
                        Console.Out.WriteLine(biezacyPozycja_bottom.x);
                        Console.Out.WriteLine(biezacyPozycja_bottom.y);


                        //Pomiary względem wody
                        start_flag = false;
                        inicjalizacja_flag = false;
                    /*
                        foreach (dvl_water odczyt in odczytyDVL_water)
                        {
                            if (odczyt.vx > -30 && odczyt.vy > -30 && odczyt.vz > -30 && odczyt.vx < 30 && odczyt.vy < 30 && odczyt.vz < 30)
                            {
                                if (config.LiczLocalTime)
                                {
                                    if (odczytyGPS_B.Count != 0)
                                        biezacyGPS_B = FindElementsByTimePostProcessing.szukajPozycjiGPS((DateTime)odczyt.local_time, odczytyGPS_B);
                                    else
                                        biezacyGPS_B = null;

                                    if (odczytyGPS_R.Count != 0)
                                        biezacyGPS_R = FindElementsByTimePostProcessing.szukajPozycjiGPS((DateTime)odczyt.local_time, odczytyGPS_R);
                                    else
                                        biezacyGPS_R = null;
                                }
                                else
                                {
                                    DateTime dataDvl = (DateTime)odczyt.device_time;
                                    // dataDvl = dataDvl.AddSeconds(14);
                                    //dataDvl = dataDvl.AddMilliseconds(500);

                                    if (odczytyGPS_B.Count != 0)
                                        biezacyGPS_B = FindElementsByTimePostProcessing.szukajPozycjiGPS(dataDvl, odczytyGPS_B);
                                    else
                                        biezacyGPS_B = null;

                                    if (odczytyGPS_R.Count != 0)
                                        biezacyGPS_R = FindElementsByTimePostProcessing.szukajPozycjiGPS(dataDvl, odczytyGPS_R);
                                    else
                                        biezacyGPS_R = null;

                                }
                                if (biezacyGPS_R != null || biezacyGPS_B != null)
                                {
                                    if (liczSatelitarnie_flag)
                                    {
                                        biezacyKurs = GeoCalc.calcSatHeading(biezacyGPS_B, biezacyGPS_R, geometricCorrection);
                                        biezacyKurs -= config.KursSatPoprawka;
                                        biezacyPozycja_water = Calkowanie.integrationDvlWater(Convert.ToSingle(biezacyKurs),
                                                    0, 0, biezacyPozycja_water, odczyt);
                                    }
                                    else
                                    {
                                        quat_ahrs najblizszy = null;
                                        if (odczytyAhrs.Count != 0)
                                        {
                                            najblizszy = FindElementsByTimePostProcessing.szukajPozycjiAhrs((DateTime)odczyt.local_time, odczytyAhrs);
                                            biezacyKurs = (double)najblizszy.yaw;

                                            if (biezacyKurs < -180)
                                                biezacyKurs += 360;
                                            if (biezacyKurs > 180)
                                                biezacyKurs -= 360;

                                            biezacyKurs -= config.KursAhrsPoprawka;
                                            biezacyPozycja_water = Calkowanie.integrationDvlWater(Convert.ToSingle(biezacyKurs),
                                                        Convert.ToSingle(najblizszy.pitch), Convert.ToSingle(najblizszy.roll), biezacyPozycja_water, odczyt);
                                        }
                                        else
                                        {
                                            biezacyKurs = 0;
                                            biezacyPozycja_water = null;
                                        }
                                    }
                                }
                                else
                                {
                                    biezacyKurs = 0;
                                    biezacyPozycja_water = null;
                                }
                                if (!inicjalizacja_flag)
                                    start_flag = true;
                                else
                                    wyniki_water.Add(biezacyPozycja_water);

                            }
                            if (start_flag)
                            {
                                //Inicjalizacja wartości początkowych
                                dvl.rotForPosition(biezacyKurs, (double)biezacyGPS_B.lat, (double)biezacyGPS_B.lon);
                                biezacyPozycja_water.lat = dvl.lat;
                                biezacyPozycja_water.lon = dvl.lon;
                                wyniki_water.Add(biezacyPozycja_water);
                                start_flag = false;
                                inicjalizacja_flag = true;
                            }
                        }*/
                        //Console.WriteLine(wyniki_water.Count);
                        //******************************************************************************
                        //Zapis do CSV


                        string path = config.PathDoZapisu + poczatek.Day + "-" + poczatek.Month + "-" + poczatek.Year + "_" + poczatek.Hour + "-" + poczatek.Minute + "-" + poczatek.Second + "_" + koniec.Hour + "-" + koniec.Minute + "-" + koniec.Second + ".csv";
                        Console.WriteLine("Zapisano plik: " + path);
                        CsvWriter csvWriter = new CsvWriter(path);
                        int licznik = 0;

                        gps dopasowanyGPS;
                        dvl_position_water dopasowanyWater = null;
                        foreach (dvl_position record in wyniki_bottom)
                        {
                            if (config.LiczLocalTime)
                            {
                                dopasowanyGPS = FindElementsByTimePostProcessing.szukajPozycjiGPS((DateTime)record.local_time, odczytyGPS_B);

                                //Obliczenie pozycji DVL na podstawie GPS baza oraz kursu
                                dvl.rotForPosition(wyniki_kurs.ElementAt(licznik), (double)dopasowanyGPS.lat, (double)dopasowanyGPS.lon);

                            //dopasowanyWater = FindElementsByTimePostProcessing.szukajPozycjiWater(((DateTime)record.local_time), wyniki_water, 0.5);
                        }
                            else
                            {
                                DateTime dataDvl = (DateTime)record.device_time;
                                // dataDvl = dataDvl.AddSeconds(14);
                                // dataDvl = dataDvl.AddMilliseconds(500);

                                dopasowanyGPS = FindElementsByTimePostProcessing.szukajPozycjiGPS(dataDvl, odczytyGPS_B);

                                //Obliczenie pozycji DVL na podstawie GPS baza oraz kursu
                                dvl.rotForPosition(wyniki_kurs.ElementAt(licznik), (double)dopasowanyGPS.lat, (double)dopasowanyGPS.lon);

                            //dopasowanyWater = FindElementsByTimePostProcessing.szukajPozycjiWater(((DateTime)record.device_time), wyniki_water, 0.5);
                            }
                            if (dopasowanyWater == null)
                            {
                                dopasowanyWater = new dvl_position_water();
                                dopasowanyWater.vx = 0;
                                dopasowanyWater.vy = 0;
                                dopasowanyWater.vz = 0;
                                dopasowanyWater.x = 0;
                                dopasowanyWater.y = 0;
                                dopasowanyWater.z = 0;
                                dopasowanyWater.lat = 0;
                                dopasowanyWater.lon = 0;
                                dopasowanyWater.alt = 0;
                                dopasowanyWater.device_time = new DateTime(2000, 1, 1, 12, 0, 0);
                            }
                            csvWriter.addNewLine(dvl, record, dopasowanyWater, wyniki_kurs.ElementAt(licznik));
                            licznik++;
                        }
                        csvWriter.Dispose();
                        Console.WriteLine("Ukonczono petle nr :" + przedzial);

                    }

                    catch (Exception ex)
                    {
                        Console.WriteLine("Pominieto petle nr :" + przedzial + "----" + ex);
                        continue;
                    }
                }
                Console.WriteLine("ZAKONCZONO OBLICZENIA");
                Console.ReadKey();
            }








        }
    }



