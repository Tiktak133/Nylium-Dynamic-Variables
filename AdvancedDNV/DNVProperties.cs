using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedDNV
{
    internal class DNVProperties
    {
        internal DNVProperties()
        {
            // Właściwości są inicjalizowane w konstruktorze, aby zapewnić, że są dostępne przed użyciem

            TypeByByte = ListOfTypes.ToDictionary(kv => kv.Value, kv => kv.Key);
        }

        public readonly byte MyVersion = 1; // AdvancedDNV Version

        /// <summary>
        /// Lista zakazanych znaków specjalnych
        /// </summary>
        internal readonly HashSet<char> forbiddenChars = new HashSet<char> { '@', '#', '$', '%', '^', '*', '{', '}', '[', ']', '|', ':', ';', '\'', '"', '<', '>', '?', '/', '\\' };

        internal readonly byte[] MetadataEncryption = new byte[] { 21, 37, 69, 62, 21, 68, 27, 15, 32, 8 , 182};
        internal readonly byte[] DNVFrameEncryption = new byte[] { 21, 72, 35, 223, 12, 23, 6, 1, 138, 12, 193 };

        /// <summary>
        /// Wszyskie dostępne typy zapisu danych
        /// </summary>
        internal readonly Dictionary<byte, Type> TypeByByte;
        internal readonly Dictionary<Type, byte> ListOfTypes = new Dictionary<Type, byte>()
        {
            //Boolen
            { typeof(bool), 1 },
            //Numbers
            { typeof(short), 2 },
            { typeof(int), 3 },
            { typeof(long), 4 },
            //String
            { typeof(string), 5 },
            //Byte
            { typeof(byte), 6 },
            //Positive Numbers
            { typeof(ushort), 7 },
            { typeof(uint), 8 },
            { typeof(ulong), 9 },
            //Fractional Numbers
            { typeof(float), 10 },
            { typeof(double), 11 },
            { typeof(decimal), 12 },
            //Date & Special
            { typeof(DateTime), 13 },
            { typeof(TimeSpan), 14 },
            { typeof(Guid), 15 },
            //Collection tables
            { typeof(byte[]), 16},
            { typeof(int[]), 17},
            { typeof(long[]), 18},
            { typeof(double[]), 19},
            //Collection tables (of variable length)
            { typeof(string[]), 20},
        };

        /// <summary>
        /// Automatyczne zapisywanie - nie dotyczy "AdvancedFrame"
        /// </summary>
        public readonly bool AutoSaveEnable = true;
        /// <summary>
        /// Częstotliwość automatycznego zapisywania - nie dotyczy "AdvancedFrame"
        /// </summary>
        public readonly int AutoSaveDelayMs = 30000;

        /*
        /// <summary>
        /// Preferuj trzymanie wartości w formie binarnej, przydatne dla dużych zbiorów danych i list, które nie muszą być odczytywane
        /// </summary>
        public readonly bool ValuesInBinaryForm = true;


        /// <summary>
        /// Edytowane wartości będa dopisywane do aktualnego pliku bez usuwania poprzednej zawartości, zawartość będzie czyszczona po uzyciu przekroczeniu "DefragmentationByteLimit"
        /// </summary>
        public readonly bool QuickSaving = true;
        /// <summary>
        /// Ile bajtów może zostać dopisanych w systemie "QuickSaving" zanim plik zostanie zdeframentowany
        /// </summary>
        public readonly int DefragmentationByteLimit = 2048;
        */

        public readonly int MinimumCompressionByteCount = 1048576; // minimalna wielkość pliku do kompresji 1MB
        public readonly byte CompressionLevel = 3; // 1-22, 1 - najszybsze, 22 - najlepsza kompresja        
    }
}
