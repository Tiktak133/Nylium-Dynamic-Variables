using System.Buffers.Binary;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// Ramka DNV do przesyłania przez sieć (bez pliku, hasła i kompresji).
    /// Układ bajtów: [2B wersja][4B długość metadanych][metadane][main], całość zaciemniona XOR.
    /// </summary>
    public class DNVFrame
    {
        private const int HeaderLength = 2 + 4;

        private readonly Container _conMain;
        private readonly Container _metadata;

        public DNVFrame()
        {
            _conMain  = new Container("Main", null);
            _metadata = new Container("MetaData", null);
        }

        /// <summary>
        /// Odtwarza ramkę z bajtów. UWAGA: tablica jest odszyfrowywana w miejscu, a wartości ramki wskazują wprost na jej
        /// fragmenty (bez kopiowania) — po przekazaniu jej tutaj NIE WOLNO jej zmieniać ani używać ponownie
        /// (np. zwracać do ArrayPool). Bufory z WatsonTcp i z odszyfrowania są za każdym razem nowe, więc to spełniają.
        /// </summary>
        /// <exception cref="FormatException">Ramka jest uszkodzona lub ucięta</exception>
        public DNVFrame(byte[] recoveryData)
        {
            DNVEncryption.SimpleEncryptInPlace(recoveryData, DNVProperties.DNVFrameEncryption);

            // Walidacja nagłówka (2026-08-14): uszkodzona/ucięta ramka dawała wcześniej OverflowException albo
            // ArgumentException, a że odbiorcy parsują ramki w handlerach async void, niezłapany wyjątek zabijał CAŁY
            // proces. Teraz każda niespójność to FormatException, który warstwa odbiorcza łapie i loguje.
            if (recoveryData.Length < HeaderLength + 4 + 4)
                throw new FormatException($"Ramka DNV za krótka: {recoveryData.Length}B (minimum {HeaderLength + 8}B).");

            ushort version  = BinaryPrimitives.ReadUInt16LittleEndian(recoveryData);
            int    metaLength = BinaryPrimitives.ReadInt32LittleEndian(recoveryData.AsSpan(2));
            long   mainLength = (long)recoveryData.Length - HeaderLength - metaLength;
            if (metaLength < 4 || mainLength < 4)
                throw new FormatException($"Nagłówek ramki DNV niespójny: len={recoveryData.Length}, wersja={version}, metaDataCount={metaLength}, mainCount={mainLength} - ramka uszkodzona lub niekompletna.");

            _metadata = Container.Parse(recoveryData, HeaderLength, metaLength, "MetaData");
            _conMain  = Container.Parse(recoveryData, HeaderLength + metaLength, (int)mainLength, "Main");
        }

        public DNVFrame(string recoveryDataString) : this(Encoding.UTF8.GetBytes(recoveryDataString)) { }

        public byte[] SendToByte()
        {
            _metadata.Value("Frame").Set(true); // metadane nie mogą być puste

            byte[] metadata = _metadata.CompileContainer(); // kilkanaście bajtów
            byte[] result;

            lock (_conMain.SyncRoot) // main zapisujemy wprost do ramki wynikowej — bez pośrednich kopii danych
            {
                int  mainLength  = _conMain.CompiledSize(out int indexSize);
                long totalLength = HeaderLength + (long)metadata.Length + mainLength;
                if (totalLength + 4096 >= int.MaxValue)
                    throw new Exception("DNV is too long");

                result = GC.AllocateUninitializedArray<byte>((int)totalLength); // każdy bajt zostaje zapisany poniżej
                BinaryPrimitives.WriteUInt16LittleEndian(result, DNVProperties.MyVersion);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), metadata.Length);
                metadata.CopyTo(result, HeaderLength);
                _conMain.CompileInto(result.AsSpan(HeaderLength + metadata.Length), indexSize);
            }

            DNVEncryption.SimpleEncryptInPlace(result, DNVProperties.DNVFrameEncryption);
            return result;
        }

        public string SendToString() => Encoding.UTF8.GetString(SendToByte());

        /// <summary> Główny kontener z danymi ramki </summary>
        public Container main => _conMain;
    }
}
