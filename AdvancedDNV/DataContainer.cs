using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// A container object that holds containers and values ​​for the DNV library
    /// </summary>
    public class Container
    {
        public string ContainerName { get; private set; }
        internal HashSet<Container> GlobalContainerSet = new();
        internal HashSet<Value> GlobalValuesSet = new();
        internal Dictionary<string, Container> ContainerList;
        internal Dictionary<string, Value> ValuesList = new();
        private Container? _parent;
        internal readonly object _collectionsLock = new object();

        // Zaktualizowno jakąś wartość - potrzebne do auto zapisu
        public delegate void ValueUpdatedEventHandler();
        public event ValueUpdatedEventHandler? ValueUpdated;

        DNVProperties _myProperties = DNVProperties.Instance;
        // Statyczna tablica współdzielona — zero alokacji per-Container przez cały czas życia procesu
        private static readonly char[] _forbiddenCharsArray = DNVProperties.ForbiddenCharsArray;

        /// <summary>
        /// Creates a container based on the supplied Byte data
        /// </summary>
        /// <param name="bytes">Data to create the container tree</param>
        internal Container(byte[] bytes, Container par, ref int lastIndex, int endIndex)
        {
            ushort containerCount = BitConverter.ToUInt16(bytes, lastIndex); lastIndex += 2;
            ushort ContainerNameCount = containerCount;

            // thisContainer — 4 jako pojemność startowa: większość kontenerów ma niewiele dzieci
            ContainerList = new Dictionary<string, Container>(4);
            ContainerName = Encoding.UTF8.GetString(bytes, lastIndex, ContainerNameCount); lastIndex += ContainerNameCount;

            this._parent = par;

            while (StepContainer(bytes, ref lastIndex))
            {
                ushort typeCount = BitConverter.ToUInt16(bytes, lastIndex);
                const ushort ValueFlag = 32768;
                if (typeCount >= ValueFlag)
                {
                    // Value
                    lastIndex += 2;
                    int ValueNameCount = typeCount - ValueFlag;

                    string ValueName = Encoding.UTF8.GetString(bytes, lastIndex, ValueNameCount); lastIndex += ValueNameCount;
                    var newValue = new Value(ValueName, this);

                    byte TypeValue = bytes[lastIndex]; lastIndex += 1;
                    uint StartIndexValue = BitConverter.ToUInt32(bytes, lastIndex); lastIndex += 4;
                    uint OccupiedSpaceValue = BitConverter.ToUInt32(bytes, lastIndex) - 1; lastIndex += 4;

                    byte[] newValueData = new byte[OccupiedSpaceValue];
                    Buffer.BlockCopy(bytes, endIndex + (int)StartIndexValue + 1, newValueData, 0, (int)OccupiedSpaceValue);

                    if (_myProperties.TypeByByte.TryGetValue(TypeValue, out Type? t) && t != null)
                    {
                        newValue.type = t;
                        newValue.value = newValueData;
                        newValue._valueLen = newValueData.Length;

                        this.InsertValue(newValue);
                    }
                    else
                    {
                        throw new Exception("Unknown Value type found in DNV data");
                    }
                }
                else if (typeCount > 0)
                {
                    //nextContainer
                    this.InsertContainer(new Container(bytes, this, ref lastIndex, endIndex));
                }
                else
                {
                    break;
                }
            }
        }

        protected internal Container(string name, Container par)
        {
            // Zakoduj tylko wtedy, gdy nie wszystkie znaki są ASCII
            var nameb = Encoding.UTF8.GetByteCount(name);
            if (nameb >= 32768)
                throw new ArgumentException("Container name is too long");

            ContainerList = new Dictionary<string, Container>();
            ContainerName = name;
            this._parent = par;
        }

        public Container this[string key]
        {
            get
            {
                lock (_collectionsLock)
                {
                    if (!this.ContainerList.ContainsKey(key))
                    {
                        // Sprawdzanie, czy ciąg znaków zawiera jakiekolwiek z zakazanych znaków
                        foreach (char c in _forbiddenCharsArray)
                        {
                            if (key.Contains(c))
                            {
                                throw new Exception("A prohibited char was used: " + c);
                            }
                        }

                        this.InsertContainerLocked(new Container(key, this));
                    }
                    return this.ContainerList[key];
                }
            }
        }

        bool StepContainer(byte[] bytes, ref int lastIndex)
        {
            if (bytes[lastIndex] == 0 && bytes[lastIndex + 1] == 0)
            {
                lastIndex += 2;
                return false;
            }
            else
            {
                return true;
            }
        }

        internal void ErrorLog(string text)
        {
            return;
        }

        internal void InsertContainer(Container con)
        {
            lock (_collectionsLock)
            {
                InsertContainerLocked(con);
            }
        }

        private void InsertContainerLocked(Container con)
        {
            if (con == null)
                throw new ArgumentNullException(nameof(con));

            // 1. Sprawdzenie zagnieżdżenia (O(1))
            if (!GlobalContainerSet.Add(con))
                throw new Exception("You cannot add the same object to this object");

            // 2. Sprawdzenie unikalności nazwy (O(1))
            if (!ContainerList.TryAdd(con.ContainerName, con))
            {
                GlobalContainerSet.Remove(con); // rollback
                throw new Exception("You cannot have a Container with the same name");
            }

            // 3. Podpięcie do wspólnej listy
            con.GlobalContainerSet = this.GlobalContainerSet;
        }


        /// <summary>
        /// An object holding values ​​for the DNV library
        /// </summary>
        /// <param name="key">Name of Value object</param>
        public Value Value(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (_collectionsLock)
            {
                // Jeden lookup zamiast TryGetValue + TryAdd — CollectionsMarshal zwraca ref do slotu w słowniku
                ref Value? slot = ref CollectionsMarshal.GetValueRefOrAddDefault(ValuesList, key, out bool existed);
                if (existed)
                    return slot!;

                // Nowy klucz — waliduj zakazane znaki dopiero teraz
                if (_forbiddenCharsArray.Length > 0 && key.IndexOfAny(_forbiddenCharsArray) >= 0)
                {
                    // Cofnij dodanie pustego slotu
                    ValuesList.Remove(key);
                    throw new ArgumentException("A prohibited char was used");
                }

                var newVal = new Value(key, this);
                slot = newVal;                     // wypełnij slot już istniejący w słowniku
                GlobalValuesSet.Add(newVal);
                return newVal;
            }
        }

        internal void InsertValue(Value val)
        {
            lock (_collectionsLock)
            {
                if (val == null)
                    throw new ArgumentNullException(nameof(val));

                // 1. globalna unikalność obiektu
                if (!GlobalValuesSet.Add(val))
                    throw new Exception("You cannot add the same object to this object");

                // 2. unikalność nazwy lokalnie (O(1))
                if (!ValuesList.TryAdd(val.ValueName, val))
                {
                    GlobalValuesSet.Remove(val); // rollback
                    throw new Exception("You cannot have a Value with the same name");
                }
            }
        }

        /// <summary>
        /// Deprecated data placement, use newer Value.Set(value) function
        /// </summary>
        /// <remarks>
        /// <b>Migration note:</b> Use <c>container.Value(name).Set(value)</c> directly.
        /// </remarks>
        [Obsolete("Use container.Value(name).Set(value) instead. This overload uses DLR dispatch via dynamic.")]
        public Container SetValue(string name, dynamic? value)
        {
            this.Value(name).Set((object?)value);
            return this;
        }

        public Container SetValues(params (string name, object? value)[] values)
        {
            HashSet<string> uniqueNames = new HashSet<string>();

            foreach (var item in values)
            {
                if (!uniqueNames.Add(item.name))
                    throw new ArgumentException($"A parameter named '{item.name}' was specified more than once.");

                this.Value(item.name).Set(item.value);
            }
            return this;
        }

        /// <summary>
        /// Deprecated data retrieval, use newer Value.Get&lt;T&gt;() function
        /// </summary>
        /// <remarks>
        /// <b>Migration note:</b> Call <c>objectV.Get&lt;T&gt;()</c> directly.
        /// </remarks>
        [Obsolete("Call objectV.Get<T>() directly instead.")]
        public T GetValue<T>(Value objectV)
        {
            return objectV.Get<T>();
        }

        /// <summary>
        /// Deprecated data retrieval, use newer Value.Get(defaultOut) function
        /// </summary>
        /// <remarks>
        /// <b>Migration note:</b> Call <c>objectV.Get&lt;T&gt;(defaultOut)</c> directly.
        /// </remarks>
        [Obsolete("Call objectV.Get<T>(defaultOut) directly instead.")]
        public dynamic GetValue(Value objectV, dynamic defaultOut)
        {
            return objectV.Get(defaultOut);
        }

        /// <summary>
        /// Deprecated data retrieval, use newer Value.Get() function
        /// </summary>
        /// <remarks>
        /// <b>Migration note:</b> Call <c>objectV.Get&lt;T&gt;()</c> directly.
        /// </remarks>
        [Obsolete("Call objectV.Get<T>() directly instead.")]
        public dynamic? GetValue(Value objectV)
        {
            return objectV.Get();
        }

        /// <remarks>
        /// <b>Migration note:</b> Use <c>container.Value(name).Get&lt;T&gt;()</c> instead.
        /// </remarks>
        [Obsolete("Use container.Value(name).Get<T>() instead.")]
        public dynamic? GetValue(string name)
        {
            return this.Value(name).Get();
        }

        /// <remarks>
        /// <b>Migration note:</b> Use <c>container.Value(name).Get&lt;T&gt;(defaultOut)</c> instead.
        /// </remarks>
        [Obsolete("Use container.Value(name).Get<T>(defaultOut) instead.")]
        public dynamic? GetValue(string name, dynamic? defaultOut)
        {
            return this.Value(name).Get(defaultOut);
        }


        public Container[] GetContainers()
        {
            lock (_collectionsLock)
                return ContainerList.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToArray();
        }

        public Value[] GetValues()
        {
            lock (_collectionsLock)
                return ValuesList.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToArray();
        }

        public void DropContainer()
        {
            // 1. Usuń wartości
            foreach (var value in GetValues())
                value.Drop();

            // 2. Usuń dzieci (rekurencyjnie)
            foreach (var child in GetContainers())
                child.DropContainer();

            // 3. Odepnij od parenta
            if (_parent != null)
            {
                lock (_parent._collectionsLock)
                    _parent.ContainerList.Remove(ContainerName);
                _parent.OnValueUpdated();
            }

            // 4. Usuń siebie z globalnego setu (NA KOŃCU, zawsze)
            if (_parent != null)
                lock (_parent._collectionsLock)
                    GlobalContainerSet.Remove(this);

            // 5. Zerwij referencje (pomaga GC)
            _parent = null;
        }


        /// <summary>
        /// Kompiluje kontener aby wykonać eksport danych
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">Zbyt dużo danych kontenera</exception>
        protected internal byte[] CompileContainer()
        {
            // Szacujemy pojemność bufora zanim zaczniemy pisać — eliminuje realokacje MemoryStream
            int estimatedSize = EstimateCompileSize(this);

            using var indexes = new System.IO.MemoryStream(estimatedSize);
            using var data    = new System.IO.MemoryStream(estimatedSize);

            GoDeeperCompileContainer(this, indexes, data);

            long totalLen = indexes.Length + data.Length + 4;
            if (totalLen >= int.MaxValue)
                throw new Exception("DNV is too long");

            // Wynik: 4 bajty rozmiaru indeksu | indeks | dane
            byte[] result = new byte[totalLen];
            int offset = 0;

            WriteInt32ToBuffer(result, offset, (int)(indexes.Length + 4)); offset += 4;

            Buffer.BlockCopy(indexes.GetBuffer(), 0, result, offset, (int)indexes.Length); offset += (int)indexes.Length;
            Buffer.BlockCopy(data.GetBuffer(),    0, result, offset, (int)data.Length);

            return result;
        }

        /// <summary>
        /// Szybki szacunek rozmiaru skompilowanego kontenera — bez alokacji, bez pisania bajtów.
        /// Przybliżenie: 2 (nameLen) + UTF8(name) + 9 (per-value index) + value.Length + 1 (header) + 2 (end marker)
        /// </summary>
        private static int EstimateCompileSize(Container con)
        {
            int size = 0;
            EstimateRecursive(con, ref size);
            return Math.Max(size, 64);
        }

        private static void EstimateRecursive(Container con, ref int size)
        {
            KeyValuePair<string, Value>[] valuesSnapshot;
            KeyValuePair<string, Container>[] containersSnapshot;
            lock (con._collectionsLock)
            {
                if (con.ValuesList.Count + con.ContainerList.Count == 0) return;
                valuesSnapshot = con.ValuesList.ToArray();
                containersSnapshot = con.ContainerList.ToArray();
            }

            // 2 (nameLen ushort) + UTF8 name bytes (ASCII fast-path: 1 byte/char)
            size += 2 + con.ContainerName.Length;

            foreach (var kv in valuesSnapshot)
            {
                var v = kv.Value;
                int valueLen = v.value?.Length ?? 0;
                // index entry: 2 (flag+nameLen) + nameLen + 1 (type) + 4 (offset) + 4 (size) = 11 + nameLen
                size += 11 + kv.Key.Length;
                // data entry: 1 (header byte) + valueLen
                size += 1 + valueLen;
            }

            foreach (var kv in containersSnapshot)
                EstimateRecursive(kv.Value, ref size);

            size += 2; // end marker
        }

        private static void WriteInt32ToBuffer(byte[] buf, int offset, int value)
        {
            buf[offset]     = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        protected private void GoDeeperCompileContainer(Container con, System.IO.MemoryStream indexes, System.IO.MemoryStream data)
        {
            var insideValues = con.ValuesList;
            var insideContainers = con.ContainerList;

            if (insideValues.Count + insideContainers.Count > 0)
            {
                string name = con.ContainerName;
                int maxBytes = Encoding.UTF8.GetMaxByteCount(name.Length);
                // ArrayPool: brak alokacji na stercie dla nazwy kontenera podczas serializacji
                byte[] rentedBuf = ArrayPool<byte>.Shared.Rent(maxBytes);
                int nameLength;
                try
                {
                    nameLength = Encoding.UTF8.GetBytes(name, 0, name.Length, rentedBuf, 0);
                    if (nameLength >= 32768)
                        throw new ArgumentException("Container name is too long");

                    // stackalloc: 2 bajty nagłówka nazwy — zero alokacji na stercie
                    Span<byte> nameLenBuf = stackalloc byte[2];
                    nameLenBuf[0] = (byte)nameLength;
                    nameLenBuf[1] = (byte)(nameLength >> 8);
                    indexes.Write(nameLenBuf);
                    indexes.Write(rentedBuf, 0, nameLength);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuf);
                }

                KeyValuePair<string, Value>[] valuesSnapshot;
                KeyValuePair<string, Container>[] containersSnapshot;
                lock (con._collectionsLock)
                {
                    valuesSnapshot = insideValues.ToArray();
                    containersSnapshot = insideContainers.ToArray();
                }

                foreach (var kv in valuesSnapshot)
                    kv.Value.GetBytes(indexes, data);

                foreach (var kv in containersSnapshot)
                    GoDeeperCompileContainer(kv.Value, indexes, data);

                indexes.WriteByte(0); indexes.WriteByte(0);
            }
        }

        /// <summary>
        /// Gdy zaktualizowano zawartość Value
        /// </summary>
        /// <param name="InvokeValue">Obiekt Value</param>
        internal virtual void OnValueUpdated()
        {
            if(_parent != null)  // Jeżeli Container posiada rodzica, przekaż event wyżej
                _parent.OnValueUpdated();
            else                // W przeciwnym razie (to jest Container Main), więc event zostaje wywołany dla niego
                ValueUpdated?.Invoke(); // Wywołanie eventu
        }

        /// <summary>
        /// Wyodrębnij wybrany Container do osobnej instancji DNVFrame
        /// </summary>
        public DNVFrame ExtractToFrame()
        {
            var ExportedFrame = new DNVFrame();

            ImportToFrame(ExportedFrame.main, this);

            return ExportedFrame;
        }

        private void ImportToFrame(Container ActualFrameContainer, Container containerToImport)
        {
            foreach (var valueI in containerToImport.GetValues())
            {
                ActualFrameContainer.Value(valueI.ValueName).Set(valueI.Get());
            }

            foreach (var containerI in containerToImport.GetContainers())
            {
                ImportToFrame(ActualFrameContainer[containerI.ContainerName], containerI);
            }
        }
    }
}
