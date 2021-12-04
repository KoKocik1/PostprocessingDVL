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
        public List<double> kursA = new List<double>();
        public List<DateTime> czas = new List<DateTime>();

        /*
        static void Main(string[] args)
        {
            Kurs program = new Kurs();
        }*/

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

                    List<gps> odczytyGPS_B = ReadDB.readGPS(poczatek, koniec, "B", conn);
                    List<gps> odczytyGPS_R = ReadDB.readGPS(poczatek, koniec, "R", conn);
                    List<initbark> geometricalPoints = config.Initbarks;
                    List<quat_ahrs> odczytyAhrs = ReadDB.readAhrs(poczatek, koniec, conn);

                    Console.WriteLine("Liczba odczytow: " + odczytyGPS_B.Count + " " + odczytyGPS_R.Count + " " + odczytyAhrs.Count);
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
                    double biezacyKursAhrs = 0;
                    double biezacyKursGps = 0;

                    List<dvl_position> wyniki_bottom = new List<dvl_position>();
                    List<dvl_position_water> wyniki_water = new List<dvl_position_water>();
                    List<double> wyniki_kurs = new List<double>();


                    //Pomiary względem dna
                    foreach (gps odczyt in odczytyGPS_B)
                    {
                        if (config.LiczLocalTime)
                        {
                            if (odczytyGPS_R.Count != 0)
                                biezacyGPS_R = FindElementsByTimePostProcessing.szukajPozycjiGPS((DateTime)odczyt.local_time, odczytyGPS_R);
                            else
                                biezacyGPS_R = null;
                        }
                        else
                        {

                            DateTime dataGps = (DateTime)odczyt.device_time;

                            if (odczytyGPS_R.Count != 0)
                            {
                                biezacyGPS_R = FindElementsByTimePostProcessing.szukajPozycjiGPS(dataGps, odczytyGPS_R);
                            }
                            else
                                biezacyGPS_R = null;

                        }

                        if (biezacyGPS_R != null && odczytyAhrs.Count!=0)
                        {
                            biezacyKursGps = GeoCalc.calcSatHeading(odczyt, biezacyGPS_R, geometricCorrection);
                            quat_ahrs xd = FindElementsByTimePostProcessing.szukajPozycjiAhrs((DateTime)odczyt.local_time, odczytyAhrs);
                            biezacyKursAhrs = (double)xd.yaw;

                            if (biezacyKursGps < 0)
                                biezacyKursGps += 360;
                            kursA.Add(biezacyKursAhrs);
                            kursG.Add(biezacyKursGps);
                            czas.Add((DateTime)xd.time);
                        }


                    }


                    //******************************************************************************
                    //Zapis do CSV

                    string path = config.PathDoZapisu + "kurs_" + poczatek.Hour + "-" + poczatek.Minute + "-" + poczatek.Second + "_" + koniec.Hour + "-" + koniec.Minute + "-" + koniec.Second + ".csv";
                    Console.WriteLine("Zapisano plik: " + path);
                    CsvWriter csvWriter = new CsvWriter(path);
                    int licznik = 0;

                    for (int i = 0; i < kursA.Count; i++)
                    {
                        csvWriter.addNewLine(kursA[i], kursG[i], czas[i]);
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

                kursA.Clear();
                kursG.Clear();
                czas.Clear();
            }

            Console.WriteLine("ZAKONCZONO OBLICZENIA");
            Console.ReadKey();
        }

    }

}

