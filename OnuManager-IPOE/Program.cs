using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;

namespace ONU_Manager
{

    enum OLT
    {
        Kotliarskia = 1,
        Zavodskia = 2
    }

    enum Protocol
    {
        PPPoE = 1,
        IPoE = 2
    }

    class ONUManager
    {

        private static readonly int port = 23;
        private static readonly string kotlIP = "10.10.110.115";
        private static readonly string zavIP = "10.10.110.120";

        private static readonly string login = "admin";
        private static readonly string password = "admin";
        private static readonly int timeout = 300;

        private static string gponInfo;
        private static string sn; // переменная для серийного номера ONU
        private static int oltNumber = 0; // номер олт
        private static int shelfNumber = 0; // номер платы на олт
        private static int ponNumber = 0; // номер пона на олт интерфейсе
        private static int slotNumber = 0; // номер слота для ону на поне

        // vlan по умолчанию
        private static readonly int kotlVlan = 1000;
        private static readonly int zavVlan = 2000;
        private static int vlan;
        private static int svlan = 500;

        private static OLT olt;
        private static Protocol protocol;

        static void Main(string[] args)
        {

            Console.WriteLine("Привiт. Вибiр протоколу. Для вибору PPPoE - 1, IPoE - 2.");
            protocol = (Protocol)Convert.ToInt32(Console.ReadLine());
            if (!Enum.IsDefined(typeof(Protocol), protocol))
            {
                Console.WriteLine("Помилка. Невірний номер ОЛТ.");

            }
            else
            {
                Console.WriteLine("Вибiр ОЛТ. Для вибору Котлярська - 1, Заводська - 2.");
                olt = (OLT)Convert.ToInt32(Console.ReadLine());

                switch (olt)
                {
                    case OLT.Kotliarskia: RegisterOnu(olt, protocol); break;
                    case OLT.Zavodskia: RegisterOnu(olt, protocol); break;
                    default: Console.WriteLine("Помилка. Невірний номер ОЛТ."); break;
                }
            }
        }

        private static void RegisterOnu(OLT olt, Protocol protocol)
        {
            // создать новое телнет подключение в зависиости от заданой ОЛТ
            TelnetConnection tc = Connect(olt);

            // залогиниться и показать ответ сервера
            Console.Write(tc.Login(login, password, timeout));
            Console.Write(tc.Read());

            var output = ShowUncfgONU(tc);
            if (output != null)
            {
                ParseOutput(output, tc, olt, protocol);
                if (slotNumber != 0)
                {
                    SetVlan(olt, protocol);
                    ConfigureOnuProfile(tc);
                    ConfigureOpticalPort(tc, protocol);
                    ConfigureEthernetPort(tc);
                    CheckConfiguration(tc);
                }
            }
            Console.ReadKey(true);
        }

        private static TelnetConnection Connect(OLT olt)
        {
            TelnetConnection tc = null;

            if (olt == OLT.Kotliarskia)
            {
                tc = new TelnetConnection(kotlIP, port);
            }
            else if (olt == OLT.Zavodskia)
            {
                tc = new TelnetConnection(zavIP, port);
            }
            return tc;
        }

        /// <summary>
        /// Возвращает данные он незагистрированных ONU елси они есть
        /// </summary>
        /// <param name="tc"></param>
        /// <returns></returns>
        private static string ShowUncfgONU(TelnetConnection tc)
        {
            // показаь незарегистрированые ONU
            tc.WriteLine("show gpon onu uncfg");

            // условие при котором ONU не надо регистрировать 
            var output = tc.Read();
            if (output.Contains("No related information to show"))
            {
                Console.WriteLine("There are nothing to configure now!");
                Console.ReadKey(true);

                return null;
            }
            return output;
        }

        private static void ParseOutput(string output, TelnetConnection tc, OLT olt, Protocol protocol)
        {
            if (output != null)
            {
                // ловим отупут сервера и парсим только инфомрацию о местонахождении ONU и ее серийный номер
                var parseOutput = output.Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < parseOutput.Length; i++)
                {
                    if (parseOutput[i].StartsWith("onu_")) gponInfo = parseOutput[i]; // номер олт, платы, пона слота дял ону
                    // серийный номер ону
                    if (parseOutput[i].StartsWith("ZTEGC") || parseOutput[i].StartsWith("MONU") || parseOutput[i].StartsWith("GPON"))
                        sn = parseOutput[i];
                }

                Console.WriteLine(gponInfo);

                // парсим информацию об ONU на номер олт, платы, пона в отдельности
                var parseGponInfo = gponInfo.Split(new char[] { '/', '_', ':' }, StringSplitOptions.RemoveEmptyEntries);
                // приводим String к int

                oltNumber = Int32.Parse(parseGponInfo[1]); // номера олт в числовом формате
                shelfNumber = Int32.Parse(parseGponInfo[2]); // номера платы в числовом формате
                ponNumber = Int32.Parse(parseGponInfo[3]); // номер пона в числовом формате

                tc.WriteLine("terminal length 0"); // снимаем ограничение на ввода для терминала

                var showGponOnuState = "show gpon onu state gpon-olt_" + oltNumber + "/"
                    + shelfNumber + "/" + ponNumber;

                tc.WriteLine(showGponOnuState); // получаем информацию о кол-ве ону и о налчиии свободных слотов на поне
                output = tc.Read();
                Console.Write(output);
                //Console.Write(tc.Read());

                Console.WriteLine("Введiть номер слота на який треба зареєструвати ОНУ:");
                slotNumber = Convert.ToInt32(Console.ReadLine());
                if (protocol == Protocol.IPoE)
                {
                    if (slotNumber > 128 || slotNumber < 1)
                    {
                        Console.WriteLine("Діапазон IPoE: 1 - 128 слоти, ви вибрали " + slotNumber + ". Спробуйте ще раз.");
                        slotNumber = 0;
                    }
                }
                if (slotNumber != 0)
                {
                    Console.Write("Номер ОЛТ: ");
                    Console.Write(oltNumber);
                    Console.WriteLine();
                    Console.Write("Номер плати: ");
                    Console.Write(shelfNumber);
                    Console.WriteLine();
                    Console.Write("Номер пону: ");
                    Console.Write(ponNumber);
                    Console.WriteLine();
                    Console.Write("Номер онушки: ");
                    Console.Write(slotNumber);
                    Console.WriteLine();
                }

            }
        }
        private static void ConfigureOnuProfile(TelnetConnection tc)
        {
            Console.WriteLine("Configure Onu Profile!");

            // влючаем configure terminal
            tc.WriteLine("Configure terminal");
            Console.Write(tc.Read());


            // заходим на необходимый интерфейс
            tc.WriteLine("interface gpon-olt_" + oltNumber + "/" + shelfNumber + "/" + ponNumber);
            tc.WriteLine("onu " + slotNumber + " type universal sn " + sn); // регистририуем ону на слоте как универсальную
            Console.Write(tc.Read());

            // ставим скорость до 1000 мб
            tc.WriteLine("onu " + slotNumber + " profile line 1000m");
            Console.Write(tc.Read());

            // ставим профель по стандарту
            tc.WriteLine("onu " + slotNumber + " profile remote standart");
            Console.Write(tc.Read());

            tc.WriteLine("exit");
            Console.Write(tc.Read());
        }

        private static void ConfigureOpticalPort(TelnetConnection tc, Protocol protocol)
        {
            Console.WriteLine("Configure Optical Port!");

            // заходим на оптический порт
            tc.WriteLine("interface gpon-onu_" + oltNumber + "/" + shelfNumber + "/" + ponNumber + ":" + slotNumber);
            Console.Write(tc.Read());

            //настраиваем влан на оптический порт
            if (protocol == Protocol.PPPoE) tc.WriteLine("service-port 1 user-vlan " + vlan + " vlan " + vlan);
            else if (protocol == Protocol.IPoE) tc.WriteLine("service-port 1 user-vlan " + vlan + " svlan " + svlan);
            Console.Write(tc.Read());

            tc.WriteLine("exit");
            Console.Write(tc.Read());
        }

        private static void ConfigureEthernetPort(TelnetConnection tc)
        {
            Console.WriteLine("Configure Ethernet Port!");

            // заходим на ethernet порт
            tc.WriteLine("pon-onu-mng gpon-onu_" + oltNumber + "/" + shelfNumber + "/" + ponNumber + ":" + slotNumber);
            Console.Write(tc.Read());

            // настраиваем  влан на ethernet порт
            tc.WriteLine("vlan port eth_0/1 mode tag vlan " + vlan);
            Console.Write(tc.Read());

            tc.WriteLine("exit");
            Console.Write(tc.Read());
        }

        private static void CheckConfiguration(TelnetConnection tc)
        {
            Console.WriteLine("Check Configuration!");
            // проверяем ли все настроено как надо
            tc.WriteLine("show running-config interface gpon-onu_" + oltNumber + "/" + shelfNumber + "/" + ponNumber + ":" + slotNumber);
            Console.Write(tc.Read());
            tc.WriteLine("show onu running config gpon-onu_" + oltNumber + "/" + shelfNumber + "/" + ponNumber + ":" + slotNumber);
            Console.Write(tc.Read());
        }

        private static void SetVlan(OLT olt, Protocol protocol)
        {
            Console.WriteLine("Set Vlan!");

            if (protocol == Protocol.PPPoE)
            {
                vlan = (shelfNumber - 1) * 16;
                int ponDif = 16 - ponNumber;

                if (olt == OLT.Kotliarskia) vlan = vlan - ponDif + kotlVlan;
                else if (olt == OLT.Zavodskia) vlan = vlan - ponDif + zavVlan;
            }
            else if (protocol == Protocol.IPoE)
            {
                vlan = ponNumber - 1;
                vlan *= 128;
                vlan += slotNumber;
                if (olt == OLT.Zavodskia) svlan += shelfNumber - 1;
                else if (olt == OLT.Kotliarskia) svlan += shelfNumber - 1 + 100;

            }

            Console.Write("Влан: ");
            Console.Write(vlan);
            Console.WriteLine();
        }
    }
}

