using AdvancedDNV;

namespace ConsoleDNVReader
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Sprawdź, czy argument został przekazany
            if (args.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(args[0]);
                OpenFile(args[0]);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Nie podano żadnego pliku do otwarcia.");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Podaj ścieżke do pliku");
                Console.Write("> ");
                string? path = Console.ReadLine();
                if (path != null)
                {
                    OpenFile(path);
                }
            }

            Console.ReadKey();
        }

        static void OpenFile(string path)
        {
            path = path.Trim();
            if (File.Exists(path))
            {
                try
                {
                    Console.Write("Hasło > ");
                    string? pass = Console.ReadLine();
                    Console.WriteLine("Ładowanie...\n");

                    DNV dnv;
                    if (pass != null && pass.Length > 0)
                        dnv = new DNV(path, pass, false);
                    else
                        dnv = new DNV(path, false);
                    dnv.Open();
                    FastInspect.ContainersAll(dnv.main);
                    FastInspect.ReadMetaData(dnv);
                }
                catch
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Wystąpił błąd podczas odczytywania pliku");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Plik w podanej lokalizacji nie isntieje");
            }
        }
    }
}
