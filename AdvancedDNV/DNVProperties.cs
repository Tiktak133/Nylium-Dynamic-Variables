namespace AdvancedDNV
{
    /// <summary>
    /// Kod typu wartości zapisywany w pliku i ramce (1 bajt na wartość).
    /// NUMERY SĄ CZĘŚCIĄ FORMATU — nie wolno ich zmieniać ani używać ponownie.
    /// </summary>
    internal enum DNVType : byte
    {
        None        = 0,  // brak wartości (tylko w pamięci, nigdy nie trafia do pliku)
        Bool        = 1,
        Short       = 2,
        Int         = 3,
        Long        = 4,
        String      = 5,
        Byte        = 6,
        UShort      = 7,
        UInt        = 8,
        ULong       = 9,
        Float       = 10,
        Double      = 11,
        Decimal     = 12,
        DateTime    = 13,
        TimeSpan    = 14,
        Guid        = 15,
        ByteArray   = 16,
        IntArray    = 17,
        LongArray   = 18,
        DoubleArray = 19,
        StringArray = 20,
    }

    internal static class DNVProperties
    {
        internal const byte   MyVersion     = 1;                   // wersja formatu AdvancedDNV
        internal const string FileSignature = "DNVFile_byNylium";  // pierwsze 16 bajtów pliku .dnv

        // Autozapis plików .dnv (nie dotyczy DNVFrame)
        internal const bool AutoSaveEnable  = true;
        internal const int  AutoSaveDelayMs = 30000;
        internal const int  AutoSaveRetryMs = 5000;   // ponowienie nieudanego autozapisu (np. plik zablokowany przez antywirusa)

        // Klucz szyfrowania pliku (PBKDF2 ~3 ms na PC, więcej na telefonie) zmieniamy nie częściej niż co tyle
        internal static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(10);

        // Kopia poprzedniej wersji pliku (.bak) — robiona przy pierwszym zapisie w sesji i potem nie częściej niż co tyle
        internal static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(10);

        // Kompresja zstd zawartości pliku
        internal const int  MinimumCompressionByteCount = 1048576; // kompresujemy dopiero od 1 MB
        internal const byte CompressionLevel            = 3;       // 1-22, 1 - najszybsza, 22 - najmocniejsza

        /// <summary> Znaki zakazane w NOWYCH nazwach kontenerów i wartości (ścieżki z ':' '/' '\' są dozwolone) </summary>
        internal static readonly System.Buffers.SearchValues<char> ForbiddenChars = System.Buffers.SearchValues.Create("@#$%*;\"?");

        // Klucze prostego zaciemniania XOR (to nie jest szyfrowanie!) — zmiana unieważnia istniejące pliki i ramki
        internal static readonly byte[] MetadataEncryption = { 21, 37, 69, 62, 21, 68, 27, 15, 32, 8, 182 };
        internal static readonly byte[] DNVFrameEncryption = { 21, 72, 35, 223, 12, 23, 6, 1, 138, 12, 193 };

        private static readonly Type?[] ClrTypes =
        {
            null,
            typeof(bool), typeof(short), typeof(int), typeof(long), typeof(string), typeof(byte),
            typeof(ushort), typeof(uint), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(DateTime), typeof(TimeSpan), typeof(Guid),
            typeof(byte[]), typeof(int[]), typeof(long[]), typeof(double[]), typeof(string[]),
        };

        /// <summary> Typ .NET odpowiadający kodowi (null dla None) </summary>
        internal static Type? ClrType(DNVType code) => ClrTypes[(int)code];

        /// <summary> Czy bajt z pliku jest znanym kodem typu </summary>
        internal static bool IsKnownType(byte code) => code >= 1 && code < ClrTypes.Length;
    }
}
