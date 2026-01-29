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
                string path = Console.ReadLine();
                OpenFile(path);
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
                    string pass = Console.ReadLine();
                    Console.WriteLine("Ładowanie...\n");

                    DNV dnv;
                    if (pass != null && pass.Length > 0)
                        dnv = new DNV(path, pass, false);
                    else
                        dnv = new DNV(path, false);
                    dnv.Open();
                    ReadContainer(dnv.main);
                    ReadMetaData(dnv.Meta);
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

        static void ReadContainer(AdvancedDNV.Container con, string old = "")
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{old}< ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"{con.ContainerName}:");
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($" ({con.GetContainers().Count()})");
            foreach (var value in con.GetValues())
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{old}  | ^");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{value.ValueName} :");
                Console.ForegroundColor = ConsoleColor.Green;
                var OutputValue = value.Get();
                if (OutputValue == null)
                {
                    Console.Write($" NULL");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($",");
                }
                else
                {
                    if (value.Type == typeof(byte[]) || value.Type == typeof(int[]) || value.Type == typeof(double[]) || value.Type == typeof(string[]))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.Write($" ({OutputValue.Length})");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($" [{(value.Type == null ? "NULL" : value.Type.ToString())}],");

                        Console.ForegroundColor = ConsoleColor.White;
                        for (int i = 0; i < Math.Min(OutputValue.Length, 30); i++)
                        {
                            Console.WriteLine($"{old}  |   - {OutputValue[i].ToString()}");
                        }

                        if (OutputValue.Length - 30 > 0)
                            Console.WriteLine($"{old}  |   (..{OutputValue.Length - 30}..)");
                    }
                    else
                    {
                        Console.Write($" {OutputValue ?? "NULL"}");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($" [{(value.Type == null ? "NULL" : value.Type.ToString())}],");
                    }
                }
            }

            foreach (var con_in in con.GetContainers())
            {
                ReadContainer(con_in, old + "  |");
            }
        }

        static void ReadMetaData(DNV.iMeta meta)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"\n- ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"MetaData:");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Autor : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{meta.author}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  < Created: ({meta.created.ToString()})\n");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Editor : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{meta.editor}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  < Last Edit: ({meta.edited.ToString()})\n");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Number of saving : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{meta.numberOfSaving}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($" times\n");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Data Size : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{NormalizeByteToText((decimal)meta.initialDataSize, "B")} ({meta.initialDataSize}B)");
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (meta.initialDataSize != meta.compressedDataSize)
                Console.Write($"   After compression {NormalizeByteToText((decimal)meta.compressedDataSize, "B")} ({meta.compressedDataSize}B)");
            Console.WriteLine("");
        }

        /// <summary>
        /// Normalizes a byte count to a human-readable string with appropriate suffixes (e.g., KB, MB, GB).
        /// </summary>
        /// <param name="count"></param>
        /// <param name="suffix"></param>
        /// <param name="prefix"></param>
        /// <returns></returns>
        static string NormalizeByteToText(decimal count, string suffix = "b", string prefix = "")
        {
            if (count >= 1125899906842624)
                return FormatNumber(count / 1125899906842624) + prefix + "P" + suffix; // Peta
            else if (count >= 1099511627776)
                return FormatNumber(count / 1099511627776) + prefix + "T" + suffix; // Tera
            else if (count >= 1073741824)
                return FormatNumber(count / 1073741824) + prefix + "G" + suffix; // Giga
            else if (count >= 1048576)
                return FormatNumber(count / 1048576) + prefix + "M" + suffix; // Mega
            else if (count >= 1024)
                return FormatNumber(count / 1024) + prefix + "k" + suffix; // Kilo
            else
                return FormatNumber(count) + prefix + suffix;
        }

        /// <summary>
        /// Formats a decimal number to a string with a maximum of 3 decimal places.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        static string FormatNumber(decimal value)
        {
            // Zaokrąglenie wartości tak, aby miała maksymalnie 3 miejsca dziesiętne
            return Math.Round(value, Math.Max(0, 3 - (int)Math.Floor(Math.Log10((double)value) + 1))).ToString();
        }
    }
}
