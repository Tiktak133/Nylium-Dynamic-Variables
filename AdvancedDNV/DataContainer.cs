using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// Kontener DNV: nazwane wartości (<see cref="AdvancedDNV.Value"/>) i zagnieżdżone kontenery.
    /// Całe drzewo (od Main w dół) dzieli jedną blokadę <see cref="SyncRoot"/>, więc można go używać z wielu wątków.
    /// </summary>
    public class Container
    {
        /// <summary> Najwyższy bit długości nazwy w indeksie: 1 = wartość, 0 = kontener. Para bajtów 0x0000 kończy kontener. </summary>
        internal const ushort ValueFlag = 0x8000;

        public string ContainerName { get; }

        internal readonly Dictionary<string, Container> ContainerList = new();
        internal readonly Dictionary<string, Value>     ValuesList    = new();
        internal readonly Lock SyncRoot; // System.Threading.Lock (.NET 9+): lżejszy niż lock(object), też z ponownym wejściem
        internal readonly int NameByteCount; // długość nazwy w UTF-8 — do wyliczenia rozmiaru przy zapisie
        private Container? _parent;

        /// <summary> Zmieniono wartość gdziekolwiek w drzewie — zdarzenie podnosi tylko korzeń (z niego korzysta autozapis) </summary>
        public delegate void ValueUpdatedEventHandler();
        public event ValueUpdatedEventHandler? ValueUpdated;

        protected internal Container(string name, Container? parent)
        {
            NameByteCount = Encoding.UTF8.GetByteCount(name);
            if (NameByteCount >= 32768)
                throw new ArgumentException("Container name is too long");

            ContainerName = name;
            _parent       = parent;
            SyncRoot      = parent?.SyncRoot ?? new Lock();
        }

        // ── Odczyt z bajtów ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Odczytuje drzewo zapisane przez <see cref="CompileContainer"/>: [4B koniec indeksu][indeks][dane].
        /// Uszkodzone dane kończą się <see cref="FormatException"/>.
        /// </summary>
        internal static Container Parse(byte[] buffer, int offset, int length, string nameIfEmpty)
        {
            if (length <= 4)
                return new Container(nameIfEmpty, null);

            int indexEnd = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4));
            if (indexEnd == 4)
                return new Container(nameIfEmpty, null); // pusty indeks — nic nie zapisano

            if (indexEnd < 4 || indexEnd > length)
                throw new FormatException($"Damaged DNV data: index end {indexEnd} outside of {length} bytes.");

            try
            {
                int pos = offset + 4;
                return new Container(buffer, null, ref pos, offset + indexEnd, offset + length);
            }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                throw new FormatException("Damaged DNV data: the container structure cannot be read.", e);
            }
        }

        /// <summary> Odczyt jednego kontenera (rekurencyjnie z dziećmi). <paramref name="pos"/> wskazuje jego nagłówek w indeksie. </summary>
        private Container(byte[] bytes, Container? parent, ref int pos, int dataStart, int dataEnd)
        {
            _parent  = parent;
            SyncRoot = parent?.SyncRoot ?? new Lock();

            NameByteCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos));  pos += 2;
            ContainerName = Encoding.UTF8.GetString(bytes, pos, NameByteCount);          pos += NameByteCount;

            while (true)
            {
                ushort header = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos));

                if (header == 0) // koniec kontenera
                {
                    pos += 2;
                    return;
                }

                if (header < ValueFlag) // podkontener — sam czyta swój nagłówek
                {
                    var child = new Container(bytes, this, ref pos, dataStart, dataEnd);
                    if (!ContainerList.TryAdd(child.ContainerName, child))
                        throw new FormatException($"Damaged DNV data: duplicated container \"{child.ContainerName}\".");
                    continue;
                }

                pos += 2;
                int valueNameLength = header - ValueFlag;
                string valueName = Encoding.UTF8.GetString(bytes, pos, valueNameLength);   pos += valueNameLength;
                byte   typeCode  = bytes[pos];                                             pos += 1;
                uint   offset    = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos)); pos += 4;
                uint   size      = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos)); pos += 4;

                if (!DNVProperties.IsKnownType(typeCode))
                    throw new FormatException($"Unknown Value type found in DNV data: {typeCode}");

                // Dane wartości: [1B nagłówka, nieużywany][size - 1 bajtów]
                long start = (long)dataStart + offset + 1;
                if (size == 0 || start + size - 1 > dataEnd)
                    throw new FormatException($"Damaged DNV data: value \"{valueName}\" points outside of the data section.");

                var value = new Value(valueName, valueNameLength, this);
                value.Load((DNVType)typeCode, bytes, (int)start, (int)size - 1); // bez kopiowania — fragment bufora
                if (!ValuesList.TryAdd(valueName, value))
                    throw new FormatException($"Damaged DNV data: duplicated value \"{valueName}\".");
            }
        }

        // ── Dostęp ──────────────────────────────────────────────────────────────────────────

        /// <summary> Podkontener o podanej nazwie (tworzony, jeśli nie istnieje) </summary>
        public Container this[string key]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(key);
                lock (SyncRoot)
                {
                    if (ContainerList.TryGetValue(key, out var existing))
                        return existing;

                    int forbidden = key.AsSpan().IndexOfAny(DNVProperties.ForbiddenChars);
                    if (forbidden >= 0)
                        throw new Exception("A prohibited char was used: " + key[forbidden]);
                    if (key.Length == 0)
                        throw new Exception("Container name cannot be empty"); // pusta nazwa zlałaby się ze znacznikiem końca

                    var created = new Container(key, this);
                    ContainerList.Add(key, created);
                    return created;
                }
            }
        }

        /// <summary> Wartość o podanej nazwie (tworzona, jeśli nie istnieje) </summary>
        public Value Value(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            lock (SyncRoot)
            {
                // Jedno przeszukanie słownika zamiast dwóch (TryGetValue + Add) — to najczęstsza operacja przy budowie ramek
                ref Value? slot = ref CollectionsMarshal.GetValueRefOrAddDefault(ValuesList, key, out bool exists);
                if (exists)
                    return slot!;

                try
                {
                    if (key.AsSpan().IndexOfAny(DNVProperties.ForbiddenChars) >= 0)
                        throw new ArgumentException("A prohibited char was used");
                    return slot = new Value(key, this);
                }
                catch
                {
                    ValuesList.Remove(key); // wycofujemy pusty wpis dodany przez GetValueRefOrAddDefault
                    throw;
                }
            }
        }

        public Container[] GetContainers()
        {
            lock (SyncRoot)
                return ContainerList.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
        }

        public Value[] GetValues()
        {
            lock (SyncRoot)
                return ValuesList.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
        }

        // ── Skróty (używane w wielu miejscach launchera, API i serwera) ─────────────────────

        /// <summary> To samo co <c>Value(name).Set(value)</c> </summary>
        public Container SetValue(string name, dynamic? value)
        {
            Value(name).Set((object?)value);
            return this;
        }

        public Container SetValues(params (string name, object? value)[] values)
        {
            var uniqueNames = new HashSet<string>();
            foreach (var (name, value) in values)
            {
                if (!uniqueNames.Add(name))
                    throw new ArgumentException($"A parameter named '{name}' was specified more than once.");
                Value(name).Set(value);
            }
            return this;
        }

        /// <summary> To samo co <c>Value(name).Get()</c> </summary>
        public dynamic? GetValue(string name) => Value(name).Get();

        /// <summary> To samo co <c>Value(name).Get(defaultOut)</c> </summary>
        public dynamic? GetValue(string name, dynamic? defaultOut) => Value(name).Get(defaultOut);

        public T GetValue<T>(Value objectV) => objectV.Get<T>();
        public dynamic GetValue(Value objectV, dynamic defaultOut) => objectV.Get(defaultOut);
        public dynamic? GetValue(Value objectV) => objectV.Get();

        // ── Usuwanie ────────────────────────────────────────────────────────────────────────

        /// <summary> Usuwa kontener z rodzica (dla Main — czyści całą zawartość) </summary>
        public void DropContainer()
        {
            Container? parent;
            lock (SyncRoot)
            {
                parent = _parent;
                if (parent == null)
                {
                    ValuesList.Clear();
                    ContainerList.Clear();
                }
                else if (parent.ContainerList.TryGetValue(ContainerName, out var registered) && registered == this)
                {
                    parent.ContainerList.Remove(ContainerName);
                }
                _parent = null;
            }
            (parent ?? this).OnValueUpdated();
        }

        internal void OnValueUpdated()
        {
            Container root = this;
            while (root._parent is { } p)
                root = p;
            root.ValueUpdated?.Invoke();
        }

        // ── Zapis do bajtów ─────────────────────────────────────────────────────────────────

        // Serializacja całego drzewa: [4B koniec indeksu][indeks][dane]. Puste kontenery i wartości są pomijane.
        // Dwa przejścia pod jedną blokadą: najpierw dokładny rozmiar, potem zapis wprost do docelowej tablicy —
        // bez MemoryStream, bez pośrednich kopii i bez osobnych tablic na nazwy.

        protected internal byte[] CompileContainer()
        {
            lock (SyncRoot) // spójna migawka całego drzewa
            {
                int size = CompiledSize(out int indexSize);
                byte[] result = GC.AllocateUninitializedArray<byte>(size); // CompileInto zapisuje każdy bajt (sprawdza to)
                CompileInto(result, indexSize);
                return result;
            }
        }

        /// <summary> Rozmiar po serializacji. Wywoływać pod blokadą <see cref="SyncRoot"/>, razem z <see cref="CompileInto"/>. </summary>
        internal int CompiledSize(out int indexSize)
        {
            long index = 0, data = 0;
            Measure(this, ref index, ref data);

            long total = 4 + index + data;
            if (total >= int.MaxValue)
                throw new Exception("DNV is too long");

            indexSize = (int)index;
            return (int)total;
        }

        /// <summary> Zapis do bufora o długości z <see cref="CompiledSize"/>. Wywoływać pod tą samą blokadą. </summary>
        internal void CompileInto(Span<byte> destination, int indexSize)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, 4 + indexSize);
            Span<byte> index = destination.Slice(4, indexSize);
            Span<byte> data  = destination[(4 + indexSize)..];

            int indexPos = 0, dataPos = 0;
            Write(this, index, ref indexPos, data, ref dataPos);

            // Bufor bywa niezainicjowany — gdyby rozmiary się rozjechały, poszłyby w świat przypadkowe bajty z pamięci
            if (indexPos != index.Length || dataPos != data.Length)
                throw new InvalidOperationException($"DNV serialization size mismatch (index {indexPos}/{index.Length}, data {dataPos}/{data.Length}).");
        }

        private static void Measure(Container con, ref long index, ref long data)
        {
            if (con.ValuesList.Count + con.ContainerList.Count == 0)
                return;

            index += 2 + con.NameByteCount + 2; // nagłówek z nazwą + znacznik końca
            foreach (var value in con.ValuesList.Values)
            {
                if (value.IsWritten)
                {
                    index += value.IndexSize;
                    data  += value.DataSize;
                }
            }
            foreach (var child in con.ContainerList.Values)
                Measure(child, ref index, ref data);
        }

        private static void Write(Container con, Span<byte> index, ref int indexPos, Span<byte> data, ref int dataPos)
        {
            if (con.ValuesList.Count + con.ContainerList.Count == 0)
                return;

            BinaryPrimitives.WriteUInt16LittleEndian(index[indexPos..], (ushort)con.NameByteCount);
            indexPos += 2;
            indexPos += Encoding.UTF8.GetBytes(con.ContainerName, index[indexPos..]);

            foreach (var value in con.ValuesList.Values)
                if (value.IsWritten)
                    value.WriteTo(index, ref indexPos, data, ref dataPos);
            foreach (var child in con.ContainerList.Values)
                Write(child, index, ref indexPos, data, ref dataPos);

            index[indexPos++] = 0; // koniec kontenera
            index[indexPos++] = 0;
        }

        // ── Wyodrębnianie ───────────────────────────────────────────────────────────────────

        /// <summary> Kopia tego kontenera (z całą zawartością) jako Main nowej ramki </summary>
        public DNVFrame ExtractToFrame()
        {
            var frame = new DNVFrame();
            lock (SyncRoot)
                CopyTree(this, frame.main);
            return frame;
        }

        private static void CopyTree(Container from, Container to)
        {
            foreach (var value in from.ValuesList.Values)
                value.CopyTo(to.Value(value.ValueName));
            foreach (var child in from.ContainerList.Values)
                CopyTree(child, to[child.ContainerName]);
        }
    }
}
