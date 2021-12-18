using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PartyMaker;

namespace PostprocessingDVL
{
    class Kurs
    {
        ConfigPostprocessing config = YamlRead.ReadConfigFile();

        //Odczyt danych
        MySqlConnection conn;

        //public DateTime (int year, int month, int day, int hour, int minute, int second, int millisecond);
        public List<DateTime> poczatekLista = new List<DateTime>();
        public List<DateTime> koniecLista = new List<DateTime>();

        public List<double> kursG = new List<double>();
        public List<double> kursA_xsens = new List<double>();
        public List<double> kursA_vn = new List<double>();
        public List<double> kursWyliczony_xsens = new List<double>();
        public List<double> kursWyliczony_vn = new List<double>();
        public List<DateTime> czas = new List<DateTime>();


        public Kurs()
        {
            //polaczenie z baza
            DBConnect connDB = new DBConnect();
            conn = connDB.OpenConnection();

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
                    Console.WriteLine(poczatek);
                    Console.WriteLine(koniec);

                    //******************************************************************************

                    List<gps> odczytyGPS_B = ReadDB.readGPSfix41(poczatek, koniec, "B", conn);
                    List<gps> odczytyGPS_R = ReadDB.readGPSfix41(poczatek, koniec, "R", conn);
                    List<initbark> geometricalPoints = config.Initbarks;
                    List<quat_ahrs> odczytyAhrs_xsens = ReadDB.readAhrsX(poczatek, koniec, conn);
                    List<quat_ahrs> odczytyAhrs_vn = ReadDB.readAhrsV(poczatek, koniec, conn);

                    Console.WriteLine("Liczba odczytow: " + odczytyGPS_B.Count + " " + odczytyGPS_R.Count + " " + odczytyAhrs_xsens.Count + " " + odczytyAhrs_vn.Count);
                    //Console.ReadKey();

                    //******************************************************************************
                    //Określenie zależności geometrycznych
                    //Przeliczenie CRP do bazowego GPS
                    initbark baseReferencePoint = null;
                    initbark rovReferencePoint = null;
                    List<initbark> pointsForRotation = new List<initbark>();
                    foreach (initbark point in geometricalPoints)
                    {
                        if (point.Identyfikator.Equals("B"))
                            baseReferencePoint = point;
                        if (point.Identyfikator.Equals("R"))
                            rovReferencePoint = point;
                        if (point.Rola.Equals("Punkt"))
                            pointsForRotation.Add(point);
                    }

                    double geometricCorrection = 0;
                    if (baseReferencePoint != null)
                    {
                        //Obliczenie poprawki na wzajemne polozenie GPS
                        if (rovReferencePoint != null)
                            geometricCorrection = GeoCalc.calcGeometricCorrection((double)baseReferencePoint.XCoordinate, (double)baseReferencePoint.YCoordinate, (double)rovReferencePoint.XCoordinate, (double)rovReferencePoint.YCoordinate);
                        else
                            Console.Out.WriteLine("Brak konfiguracji GPS rov. Sprawdź bazę danych - tablica initbark");
                    }
                    Console.WriteLine("korekcja gps: " + geometricCorrection);

                        //******************************************************************************
                        ////Właściwe obliczenia

                    bool start_flag = false;
                    bool inicjalizacja_flag = false;

                    gps biezacyGPS_R = null;
                    double biezacyKursAhrs_xsens = 0;
                    double biezacyKursAhrs_vn = 0;
                    double biezacyKursGps = 0;
                    bool jestFix = false;
                    bool seriaBrakuFix = false;
                    double roznicaXsens = 0;
                    double roznicaVN = 0;

                    List<dvl_position> wyniki_bottom = new List<dvl_position>();
                    List<dvl_position_water> wyniki_water = new List<dvl_position_water>();
                    List<double> wyniki_kurs = new List<double>();


                    
                    foreach (gps odczyt in odczytyGPS_B)
                    {
                        if (odczyt.fix == 4)
                            jestFix = true;
                        else
                            jestFix = false;

                        if (odczytyGPS_R.Count != 0 && jestFix)
                        {
                            biezacyGPS_R = FindElementsByTimePostProcessing.szukajPozycjiGPS((DateTime)odczyt.local_time, odczytyGPS_R);

                            if (biezacyGPS_R.fix != 4)
                                jestFix = false;
                            else
                                seriaBrakuFix = false;
                            
                        }
                        else
                        {
                            biezacyGPS_R = null;
                            jestFix = false;
                        }


                        if (odczytyAhrs_xsens.Count!=0 && odczytyAhrs_vn.Count!=0)
                        {
                            if (jestFix)
                                biezacyKursGps = GeoCalc.calcSatHeading(odczyt, biezacyGPS_R, geometricCorrection);
                            else
                                biezacyKursGps = 0;

                            quat_ahrs xd = FindElementsByTimePostProcessing.szukajPozycjiAhrs((DateTime)odczyt.local_time, odczytyAhrs_xsens);
                            biezacyKursAhrs_xsens = (double)xd.yaw;

                            quat_ahrs xd1 = FindElementsByTimePostProcessing.szukajPozycjiAhrs((DateTime)odczyt.local_time, odczytyAhrs_vn);
                            biezacyKursAhrs_vn = (double)xd1.yaw;

                            if (biezacyKursGps < 0)
                                biezacyKursGps += 360;
                            if (biezacyKursAhrs_vn < 0)
                                biezacyKursAhrs_vn += 360;

                            kursA_xsens.Add(biezacyKursAhrs_xsens);
                            kursA_vn.Add(biezacyKursAhrs_vn);
                            kursG.Add(biezacyKursGps);
                            czas.Add((DateTime)odczyt.local_time);

                            if (jestFix)
                            {
                                kursWyliczony_xsens.Add(biezacyKursGps);
                                kursWyliczony_vn.Add(biezacyKursGps);
                            }
                            else
                            {
                                if (!seriaBrakuFix)
                                {
                                    seriaBrakuFix = true;
                                    roznicaXsens = kursG[kursG.Count-2] - biezacyKursAhrs_xsens;
                                    roznicaVN = kursG[kursG.Count - 2] - biezacyKursAhrs_vn;
                                }

                                double a = biezacyKursAhrs_xsens + roznicaXsens;
                                if (a < 0)
                                    a += 360;
                                kursWyliczony_xsens.Add(a);

                                a = biezacyKursAhrs_vn + roznicaVN;
                                if (a < 0)
                                    a += 360;
                                kursWyliczony_vn.Add(a);

                            }

                        }


                    }


                    //******************************************************************************
                    //Zapis do CSV

                    string path = config.PathDoZapisu + "kurs_" + poczatek.Hour + "-" + poczatek.Minute + "-" + poczatek.Second + "_" + koniec.Hour + "-" + koniec.Minute + "-" + koniec.Second + ".csv";
                    Console.WriteLine("Zapisano plik: " + path);
                    CsvWriter csvWriter = new CsvWriter(path);
                    int licznik = 0;

                    int powtorzenia = (kursA_vn.Count <= kursA_xsens.Count) ? kursA_vn.Count : kursA_xsens.Count;
                    if (kursWyliczony_vn.Count < powtorzenia) powtorzenia = kursWyliczony_vn.Count;
                    if (kursWyliczony_xsens.Count < powtorzenia) powtorzenia = kursWyliczony_xsens.Count;

                    for (int i = 0; i < powtorzenia; i++)
                    {
                        csvWriter.addNewLine(kursA_xsens[i], kursA_vn[i], kursG[i], czas[i],kursWyliczony_xsens[i],kursWyliczony_vn[i]);
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

                kursA_xsens.Clear();
                kursA_vn.Clear();
                kursG.Clear();
                kursWyliczony_vn.Clear();
                kursWyliczony_xsens.Clear();
                czas.Clear();
            }

            Console.WriteLine("ZAKONCZONO OBLICZENIA");
            Console.ReadKey();
        }

    }

}

