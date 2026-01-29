using System;
using System.Collections.Generic;
using System.Linq;
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
        internal Dictionary<string, Value> ValuesList = new Dictionary<string, Value>();
        private Container? _parent;

        // Zaktualizowno jakąś wartość - potrzebne do auto zapisu
        public delegate void ValueUpdatedEventHandler();
        public event ValueUpdatedEventHandler ValueUpdated;

        DNVProperties _myProperties = new DNVProperties();
        private readonly char[] _forbiddenCharsArray;

        /// <summary>
        /// Creates a container based on the supplied Byte data
        /// </summary>
        /// <param name="bytes">Data to create the container tree</param>
        internal Container(byte[] bytes, Container par, advInt32 lastIndex, int endIndex, char[]? forbiddenCharsCache = null)
        {
            if (forbiddenCharsCache != null)
                _forbiddenCharsArray = forbiddenCharsCache;
            else
                _forbiddenCharsArray = _myProperties.forbiddenChars.ToArray();

            ushort containerCount = BitConverter.ToUInt16(bytes, lastIndex.Get()); lastIndex.Add(2);
            ushort ContainerNameCount = containerCount;

            // thisContainer
            ContainerList = new Dictionary<string, Container>();
            ContainerName = Encoding.UTF8.GetString(bytes, lastIndex.Get(), ContainerNameCount); lastIndex.Add(ContainerNameCount);

            this._parent = par;

            while (StepContainer(bytes, lastIndex))
            {
                ushort typeCount = BitConverter.ToUInt16(bytes, lastIndex.Get());
                const ushort ValueFlag = 32768;
                if (typeCount >= ValueFlag)
                {
                    // Value
                    lastIndex.Add(2);
                    int ValueNameCount = Convert.ToUInt16(typeCount - 32768);           

                    string ValueName = Encoding.UTF8.GetString(bytes, lastIndex.Get(), ValueNameCount); lastIndex.Add(ValueNameCount);
                    var newValue = new Value(ValueName, this);

                    byte TypeValue = bytes[lastIndex.Get()]; lastIndex.Add(1);
                    uint StartIndexValue = BitConverter.ToUInt32(bytes, lastIndex.Get()); lastIndex.Add(4);
                    uint OccupiedSpaceValue = BitConverter.ToUInt32(bytes, lastIndex.Get()) - 1; lastIndex.Add(4);

                    byte[] newValueData = new byte[OccupiedSpaceValue];
                    Buffer.BlockCopy(bytes, endIndex + (int)StartIndexValue + 1, newValueData, 0, (int)OccupiedSpaceValue); // UWAGA +1 i -1 są ze względu na pierwszy byte zapisany w każdej zmiennej NA TEN MOMENT NIE UŻYWANY

                    if (_myProperties.TypeByByte.TryGetValue(TypeValue, out Type? t) && t != null)
                    {
                        newValue.type = t;
                        newValue.value = newValueData;

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
                    this.InsertContainer(new Container(bytes, this, lastIndex, endIndex, _forbiddenCharsArray));
                }
                else
                {
                    break;
                }                  
            }
        }

        protected internal Container(string name, Container par)
        {
            _forbiddenCharsArray = _myProperties.forbiddenChars.ToArray();

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

                    this.InsertContainer(new Container(key, this));
                }
                return this.ContainerList[key];
            }
        }

        bool StepContainer(byte[] bytes, advInt32 lastIndex)
        {
            if (bytes[lastIndex.Get()] == 0 && bytes[lastIndex.Get() + 1] == 0)
            {
                lastIndex.Add(2);
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

            if (ValuesList.TryGetValue(key, out var existing))
                return existing;

            // Szybkie sprawdzenie zakazanych znaków bez alokacji
            if (_forbiddenCharsArray.Length > 0 && key.IndexOfAny(_forbiddenCharsArray) >= 0)
                throw new ArgumentException("A prohibited char was used");

            var newVal = new Value(key, this);
            InsertValue(newVal);
            return newVal;
        }

        internal void InsertValue(Value val)
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

        /// <summary>
        /// Deprecated data placement, use newer Value.Set(value) function
        /// </summary>
        public Container SetValue(string name, dynamic value)
        {
            this.Value(name).Set(value);
            return this;
        }

        public Container SetValues(params (string name, dynamic value)[] values)
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
        /// Deprecated data retrieval, use newer Value.Get<T>() function
        /// </summary>
        public T GetValue<T>(Value objectV)
        {
            return objectV.Get<T>();
        }

        /// <summary>
        /// Deprecated data retrieval, use newer Value.Get(defaultOut) function
        /// </summary>
        public dynamic GetValue(Value objectV, dynamic defaultOut)
        {
            return objectV.Get(defaultOut);
        }

        /// <summary>
        /// Deprecated data retrieval, use newer Value.Get() function
        /// </summary>
        public dynamic GetValue(Value objectV)
        {
            return objectV.Get();
        }

        public dynamic GetValue(string name)
        {
            return this.Value(name).Get();
        }

        public dynamic GetValue(string name, dynamic defaultOut)
        {
            return this.Value(name).Get(defaultOut);
        }


        public Container[] GetContainers()
        {
            return ContainerList.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToArray();
        }

        public Value[] GetValues()
        {
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
                _parent.ContainerList.Remove(ContainerName);
                _parent.OnValueUpdated();
            }

            // 4. Usuń siebie z globalnego setu (NA KOŃCU, zawsze)
            GlobalContainerSet.Remove(this);

            // 5. Zerwij referencje (pomaga GC)
            _parent = null;
        }


        /// <summary>
        /// Kompiluje kontener aby wykonać eksport danych
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">Zbyt dużo danych kontenera</exception>
        protected internal List<byte> CompileContainer()
        {
            List<byte> result = new List<byte>();
            List<byte> indexes = new List<byte>();
            List<byte> data = new List<byte>();

            GoDeeperCompileContainer(this, indexes, data);

            if (indexes.Count() + data.Count() + 4 >= int.MaxValue)
                throw new Exception("DNV is too long");

            result.AddRange(BitConverter.GetBytes(indexes.Count()+4)); //Wielkość całego indexu - x4byte

            result.AddRange(indexes);
            result.AddRange(data);
            result.AddRange(data);
            return result;
        }

        protected private void GoDeeperCompileContainer(Container con, List<byte> indexes, List<byte> data)
        {
            var insideValues = con.GetValues();
            var insideContainers = con.GetContainers();

            if (insideValues.Count() + insideContainers.Count() > 0) // Jeżeli w środku Containera coś się znajduje, w przeciwnym razie nie podlega kompilacji (wyginie)
            {
                string name = con.ContainerName;
                byte[] stringBytes = Encoding.UTF8.GetBytes(name);
                ushort nameLength = Convert.ToUInt16(stringBytes.Count());
                if (nameLength >= 32768)
                    throw new ArgumentException("Container name is too long");

                indexes.AddRange(BitConverter.GetBytes(nameLength)); // Dodawanie długości nazwy kontenera
                indexes.AddRange(stringBytes); // Dodawanie nazwy w UTF8 kontenera

                
                foreach (var value in insideValues)
                {
                    value.GetBytes(indexes, data);
                }

                foreach (var con_in in insideContainers)
                {
                    GoDeeperCompileContainer(con_in, indexes, data);
                }

                indexes.Add(0); indexes.Add(0); // Informacja o końcu kontenera
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
