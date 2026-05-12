namespace AdvancedDNV
{
    public static class FastInspect
    {
        // ── Color Helpers ─────────────────────────────────────────────────────────────────

        private static void Cw(ConsoleColor color, string text)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }

        private static void Cwl(ConsoleColor color, string text)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
        }

        // ── Public Methods ────────────────────────────────────────────────────────────────

        public static void ReadMetaData(DNV dnv)
        {
            ConsoleColor saved = Console.ForegroundColor;
            if (dnv.Meta == null)
            {
                Cwl(ConsoleColor.Red, "[!] no meta data");
                return;
            }

            DNV.iMeta meta = dnv.Meta;

            string sizeStr = $"{NormalizeByteToText((decimal)meta.initialDataSize, "B")} ({meta.initialDataSize} B)";
            if (meta.initialDataSize != meta.compressedDataSize)
                sizeStr += $" -> {NormalizeByteToText((decimal)meta.compressedDataSize, "B")} ({meta.compressedDataSize} B) compressed";

            Console.WriteLine();

            var rows = new (string Label, string Value)[]
            {
                ("Author",  $"{meta.author}  <- Created: {meta.created}"),
                ("Editor",  $"{meta.editor}  <- Last Edit: {meta.edited}"),
                ("Savings", $"{meta.numberOfSaving} times"),
                ("Size",    sizeStr),
            };

            // innerWidth = number of characters between ║ and ║ (and between ╔/╚ and ╗/╝)
            // each row renders as: ║ + "  {label,-8}: {value}" + padding + ║
            const string title = " MetaData ";
            int innerWidth = rows.Max(r => 2 + Math.Max(r.Label.Length, 8) + 2 + r.Value.Length + 1);
            innerWidth = Math.Max(innerWidth, title.Length + 2);

            string top    = "╔═" + title + new string('═', innerWidth - title.Length - 1) + "╗";
            string bottom = "╚" + new string('═', innerWidth) + "╝";

            Cwl(ConsoleColor.Cyan, top);
            foreach (var row in rows)
                PrintMetaRow(row.Label, row.Value, innerWidth);
            Cwl(ConsoleColor.Cyan, bottom);
            Console.ForegroundColor = saved;
        }

        public static void ContainersAll(Container con)
        {
            ConsoleColor saved = Console.ForegroundColor;
            Console.WriteLine();
            PrintContainer(con, "", "");
            Console.ForegroundColor = saved;
        }

        /// <summary>
        /// Displays only the direct values of a single container (non-recursive).
        /// </summary>
        public static void ContainerValues(Container con)
        {
            ConsoleColor saved = Console.ForegroundColor;
            Value[] values = con.GetValues();

            Cw(ConsoleColor.White, "┌─ ");
            Cwl(ConsoleColor.Blue, con.ContainerName);

            for (int i = 0; i < values.Length; i++)
                PrintValueLine(values[i], "", i == values.Length - 1 ? "└── " : "├── ");

            Console.ForegroundColor = saved;
        }

        /// <summary>
        /// Displays the full container tree showing only names (no values).
        /// </summary>
        public static void ContainersOnlyStructure(Container con, string prefix = "")
        {
            ConsoleColor saved = Console.ForegroundColor;
            PrintStructure(con, prefix, "");
            Console.ForegroundColor = saved;
        }

        // ── Private Methods ────────────────────────────────────────────────────────────────

        private static void PrintMetaRow(string label, string value, int innerWidth)
        {
            string content = $"  {label,-8}: {value}";
            int padding = innerWidth - content.Length; // fill remaining space before closing ║
            Cw(ConsoleColor.Cyan,   "║");
            Cw(ConsoleColor.Yellow, $"  {label,-8}: ");
            Cw(ConsoleColor.Green,  value);
            Cw(ConsoleColor.Cyan,   new string(' ', Math.Max(0, padding)));
            Cwl(ConsoleColor.Cyan,  "║");
        }

        /// <summary>
        /// Recursively prints a container and all its children as a tree.
        /// </summary>
        /// <param name="con">Container to print</param>
        /// <param name="indent">Current indentation string passed from parent</param>
        /// <param name="connector">Branch symbol used for this container's header line</param>
        private static void PrintContainer(Container con, string indent, string connector)
        {
            Value[]     values     = con.GetValues();
            Container[] containers = con.GetContainers();

            // ── Container header ──
            Cw(ConsoleColor.White,   indent + connector + "< ");
            Cw(ConsoleColor.Blue,    con.ContainerName);
            Cwl(ConsoleColor.DarkGray, $"  ({values.Length}v, {containers.Length}c)");

            // Children share the same indent level; extend it based on whether this node is last
            string childIndent = indent + (connector == "└── " ? "    " : connector == "" ? "" : "│   ");

            int total = values.Length + containers.Length;
            int index = 0;

            foreach (Value val in values)
            {
                bool last = (++index == total);
                PrintValueLine(val, childIndent, last ? "└── " : "├── ");
            }

            foreach (Container sub in containers)
            {
                bool last = (++index == total);
                PrintContainer(sub, childIndent, last ? "└── " : "├── ");
            }
        }

        private static void PrintValueLine(Value value, string indent, string connector)
        {
            Cw(ConsoleColor.White,  indent + connector);
            Cw(ConsoleColor.Yellow, value.ValueName);
            Cw(ConsoleColor.White,  " : ");

            var output = value.Get();
            if (output == null)
            {
                Cw(ConsoleColor.DarkGray, "NULL");
            }
            else if (value.Type == typeof(byte[]) || value.Type == typeof(int[]) ||
                     value.Type == typeof(double[]) || value.Type == typeof(string[]))
            {
                Cw(ConsoleColor.DarkYellow, $"[{output.Length} items]");
            }
            else
            {
                Cw(ConsoleColor.Green, $"{output}");
            }

            Cwl(ConsoleColor.DarkGray, $"  [{value.Type?.Name ?? "NULL"}]");
        }

        private static void PrintStructure(Container con, string indent, string connector)
        {
            Value[]     values     = con.GetValues();
            Container[] containers = con.GetContainers();

            Cw(ConsoleColor.White, indent + connector + "< ");
            Cwl(ConsoleColor.Blue, con.ContainerName);

            string childIndent = indent + (connector == "└── " ? "    " : connector == "" ? "" : "│   ");

            int total = values.Length + containers.Length;
            int index = 0;

            foreach (Value val in values)
            {
                bool last = (++index == total);
                Cw(ConsoleColor.White,  childIndent + (last ? "└── " : "├── ") );
                Cwl(ConsoleColor.Yellow, val.ValueName);
            }

            foreach (Container sub in containers)
            {
                bool last = (++index == total);
                PrintStructure(sub, childIndent, last ? "└── " : "├── ");
            }
        }

        /// <summary>
        /// Normalizes a byte count to a human-readable string with appropriate unit suffix.
        /// </summary>
        private static string NormalizeByteToText(decimal count, string suffix = "B", string prefix = "")
        {
            if (count >= 1125899906842624M)
                return FormatNumber(count / 1125899906842624M) + prefix + "P" + suffix;
            else if (count >= 1099511627776M)
                return FormatNumber(count / 1099511627776M) + prefix + "T" + suffix;
            else if (count >= 1073741824M)
                return FormatNumber(count / 1073741824M) + prefix + "G" + suffix;
            else if (count >= 1048576M)
                return FormatNumber(count / 1048576M) + prefix + "M" + suffix;
            else if (count >= 1024M)
                return FormatNumber(count / 1024M) + prefix + "k" + suffix;
            else
                return FormatNumber(count) + prefix + suffix;
        }

        /// <summary>
        /// Formats a decimal number with a maximum of 3 significant decimal places.
        /// </summary>
        private static string FormatNumber(decimal value)
        {
            return Math.Round(value, Math.Max(0, 3 - (int)Math.Floor(Math.Log10((double)value) + 1))).ToString();
        }
    }
}
