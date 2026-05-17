using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AdvancedDNV
{
    /// <summary>
    /// An object holding values ​​for the DNV library
    /// </summary>
    public class Value
    {
        public string ValueName { get; private set; }
        /// <summary>
        /// Object type, value type ​​of this Value
        /// </summary>
        public Type? Type { get { return type; } }
        private Container _parent;

        internal Type? type = null; //Typ wartości
        internal byte[]? value;

        DNVProperties _myProperties = DNVProperties.Instance;
        private readonly Lock _engagedElementLock = new Lock(); // System.Threading.Lock (.NET 9+) – szybszy niż lock(object)

        internal Value(string name, Container par)
        {
            var nameb = Encoding.UTF8.GetByteCount(name);
            if (nameb >= 32768)
                throw new ArgumentException("Value name is too long");

            ValueName = name;
            _parent = par;
        }

        /// <summary>
        /// Check if the value is assigned
        /// </summary>
        public bool IsSet()
        {
            lock (_engagedElementLock)
            {
                if (_parent == null) // Jeżeli Value zostanie Droped do momentu wywołania tej metody
                    return false;

                return (value != null && type != null);
            }
        }

        /// <summary>
        /// Adding/Summing a value to an already stored value in an object, doesn't work with all types
        /// </summary>
        /// <param name="value"></param>
        public Value Add(dynamic? value)
        {
            if (value == null)
            {
                Set(value);
                return this;
            }

            lock (_engagedElementLock)
            {
                if (_parent != null) // Jeżeli Value zostanie Droped do momentu wywołania tej metody
                {
                    try
                    {
                        if (_myProperties.ListOfTypes.ContainsKey(value.GetType()))
                        {
                            var oldValue = Get(); //Pobieranie aktualnie zapisanej wartości

                            if (oldValue is bool)
                            {
                                this.value = BitConverter.GetBytes(value);
                                type = typeof(bool);
                            }
                            else if (oldValue is int || oldValue is uint || oldValue is short || oldValue is ushort || oldValue is long || oldValue is ulong || oldValue is byte)
                            {
                                oldValue = oldValue + value;

                                dynamic newValue = 0;
                                if (oldValue < 0)
                                {
                                    if (oldValue < short.MinValue)
                                    {
                                        if (oldValue < int.MinValue)
                                            newValue = (long)oldValue;
                                        else
                                            newValue = (int)oldValue;
                                    }
                                    else
                                        newValue = (short)oldValue;
                                    this.value = BitConverter.GetBytes(newValue);
                                    type = newValue.GetType();
                                }
                                else
                                {
                                    if (oldValue > byte.MaxValue)
                                    {
                                        if (oldValue > ushort.MaxValue)
                                        {
                                            if (oldValue > uint.MaxValue)
                                                newValue = (ulong)oldValue;
                                            else
                                                newValue = (uint)oldValue;
                                        }
                                        else
                                            newValue = (ushort)oldValue;
                                        this.value = BitConverter.GetBytes(newValue);
                                        type = newValue.GetType();
                                    }
                                    else
                                    {
                                        newValue = (byte)oldValue;
                                        this.value = new byte[] { newValue };
                                        type = newValue.GetType();
                                    }
                                }
                            }
                            else if (oldValue is null && (value is int || value is uint || value is short || value is ushort || value is long || value is ulong || value is byte))
                            {
                                oldValue = value; //Domyślnie 0 + value (nowa wartość)

                                dynamic newValue = 0;
                                if (oldValue < 0)
                                {
                                    if (oldValue < short.MinValue)
                                    {
                                        if (oldValue < int.MinValue)
                                            newValue = (long)oldValue;
                                        else
                                            newValue = (int)oldValue;
                                    }
                                    else
                                        newValue = (short)oldValue;
                                    this.value = BitConverter.GetBytes(newValue);
                                    type = newValue.GetType();
                                }
                                else
                                {
                                    if (oldValue > byte.MaxValue)
                                    {
                                        if (oldValue > ushort.MaxValue)
                                        {
                                            if (oldValue > uint.MaxValue)
                                                newValue = (ulong)oldValue;
                                            else
                                                newValue = (uint)oldValue;
                                        }
                                        else
                                            newValue = (ushort)oldValue;
                                        this.value = BitConverter.GetBytes(newValue);
                                        type = newValue.GetType();
                                    }
                                    else
                                    {
                                        newValue = (byte)oldValue;
                                        this.value = new byte[] { newValue };
                                        type = newValue.GetType();
                                    }
                                }
                            }
                            else
                            {
                                try
                                {
                                    string tryS = oldValue + value.ToString();
                                    this.value = Encoding.UTF8.GetBytes(tryS);
                                    type = typeof(string);
                                }
                                catch
                                {
                                    type = null;
                                    _parent.ErrorLog("Type of this variable is not supported");
                                    return this;
                                }
                            }

                            _parent.OnValueUpdated();
                        }
                        else
                        {
                            _parent.ErrorLog("Type of this variable is not supported");
                        }
                    }
                    catch
                    {
                        _parent.ErrorLog("Type of this variable is not supported");
                        //Nie poprawny typ
                    }
                }
            }
            return this;
        }

        /// <summary>
        // ── Typowane przeciążenia Set – eliminują dynamic/DLR/boxing ──────────

        public Value Set(bool value)
        {
            lock (_engagedElementLock)
            {
                this.value = BitConverter.GetBytes(value);
                type = typeof(bool);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(int value)
        {
            lock (_engagedElementLock)
            {
                if (value >= 0 && value <= byte.MaxValue)
                {
                    this.value = new byte[] { (byte)value };
                    type = typeof(byte);
                }
                else if (value >= short.MinValue && value <= short.MaxValue)
                {
                    this.value = BitConverter.GetBytes((short)value);
                    type = typeof(short);
                }
                else
                {
                    this.value = BitConverter.GetBytes(value);
                    type = typeof(int);
                }
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(long value)
        {
            lock (_engagedElementLock)
            {
                if (value >= 0 && value <= byte.MaxValue)
                {
                    this.value = new byte[] { (byte)value };
                    type = typeof(byte);
                }
                else if (value >= short.MinValue && value <= short.MaxValue)
                {
                    this.value = BitConverter.GetBytes((short)value);
                    type = typeof(short);
                }
                else if (value >= int.MinValue && value <= int.MaxValue)
                {
                    this.value = BitConverter.GetBytes((int)value);
                    type = typeof(int);
                }
                else
                {
                    this.value = BitConverter.GetBytes(value);
                    type = typeof(long);
                }
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(double value)
        {
            lock (_engagedElementLock)
            {
                if ((double)(float)value == value)
                {
                    this.value = BitConverter.GetBytes((float)value);
                    type = typeof(float);
                }
                else
                {
                    this.value = BitConverter.GetBytes(value);
                    type = typeof(double);
                }
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(float value)
        {
            lock (_engagedElementLock)
            {
                this.value = BitConverter.GetBytes(value);
                type = typeof(float);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(string value)
        {
            lock (_engagedElementLock)
            {
                this.value = Encoding.UTF8.GetBytes(value);
                type = typeof(string);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(Guid value)
        {
            lock (_engagedElementLock)
            {
                if (value == Guid.Empty) throw new ArgumentException("Guid cannot be empty");
                this.value = value.ToByteArray();
                type = typeof(Guid);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(DateTime value)
        {
            lock (_engagedElementLock)
            {
                this.value = BitConverter.GetBytes(value.Ticks);
                type = typeof(DateTime);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(TimeSpan value)
        {
            lock (_engagedElementLock)
            {
                this.value = BitConverter.GetBytes(value.Ticks);
                type = typeof(TimeSpan);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(byte value)
        {
            lock (_engagedElementLock)
            {
                this.value = new byte[] { value };
                type = typeof(byte);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(byte[] value)
        {
            lock (_engagedElementLock)
            {
                this.value = value;
                type = typeof(byte[]);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(int[] value)
        {
            lock (_engagedElementLock)
            {
                var bytes = new byte[value.Length * sizeof(int)];
                Buffer.BlockCopy(value, 0, bytes, 0, bytes.Length);
                this.value = bytes;
                type = typeof(int[]);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(double[] value)
        {
            lock (_engagedElementLock)
            {
                var bytes = new byte[value.Length * sizeof(double)];
                Buffer.BlockCopy(value, 0, bytes, 0, bytes.Length);
                this.value = bytes;
                type = typeof(double[]);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        // ── Oryginalna metoda Set(dynamic?) – ścieżka awaryjna dla pozostałych typów ─

        /// Setting a value to an object
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public Value Set(dynamic? value)
        {
            lock (_engagedElementLock) // Blokuje dostęp do Value, gdy jest on używany przez inny wątek
            {
                if (value == null)
                {
                    value = null;
                    type = null;
                    return this;
                }

                if (_parent != null) // Jeżeli Value zostanie Droped do momentu wywołania tej metody
                {
                    try
                    {
                        if (_myProperties.ListOfTypes.ContainsKey(value.GetType()))
                        {
                            type = value.GetType();
                            if (value is bool)
                            {
                                this.value = BitConverter.GetBytes(value);
                                type = typeof(bool);
                            }
                            else if (value is int || value is uint || value is short || value is ushort || value is long || value is ulong)
                            {
                                dynamic newValue = 0;
                                if (value < 0)
                                {
                                    if (value < short.MinValue)
                                    {
                                        if (value < int.MinValue)
                                            newValue = (long)value;
                                        else
                                            newValue = (int)value;
                                    }
                                    else
                                        newValue = (short)value;
                                    this.value = BitConverter.GetBytes(newValue);
                                }
                                else
                                {
                                    if (value > byte.MaxValue)
                                    {
                                        if (value > ushort.MaxValue)
                                        {
                                            if (value > uint.MaxValue)
                                                newValue = (ulong)value;
                                            else
                                                newValue = (uint)value;
                                        }
                                        else
                                            newValue = (ushort)value;
                                        this.value = BitConverter.GetBytes(newValue);
                                    }
                                    else
                                    {
                                        newValue = (byte)value;
                                        this.value = new byte[] { newValue };
                                    }

                                }
                                type = newValue.GetType();
                            }
                            else if (value is float || value is double || value is decimal)
                            {
                                // Wybór najwłaściwszego typu zmiennoprzecinkowego
                                if (value is decimal decimalValue)
                                {
                                    // Sprawdzenie, czy decimalValue może być reprezentowane jako float lub double
                                    if ((decimal)(float)decimalValue == decimalValue)
                                    {
                                        // Reprezentacja jako float
                                        this.value = BitConverter.GetBytes((float)decimalValue);
                                        type = typeof(float);
                                    }
                                    else if ((decimal)(double)decimalValue == decimalValue)
                                    {
                                        // Reprezentacja jako double
                                        this.value = BitConverter.GetBytes((double)decimalValue);
                                        type = typeof(double);
                                    }
                                    else
                                    {
                                        // Reprezentacja jako decimal
                                        int[] bits = decimal.GetBits(decimalValue);
                                        this.value = new byte[16];
                                        Buffer.BlockCopy(bits, 0, this.value, 0, 16);
                                        type = typeof(decimal);
                                    }
                                }
                                else if (value is double doubleValue)
                                {
                                    // Sprawdzenie, czy doubleValue może być reprezentowane jako float
                                    if ((double)(float)doubleValue == doubleValue)
                                    {
                                        // Reprezentacja jako float
                                        this.value = BitConverter.GetBytes((float)doubleValue);
                                        type = typeof(float);
                                    }
                                    else
                                    {
                                        // Reprezentacja jako double
                                        this.value = BitConverter.GetBytes(doubleValue);
                                        type = typeof(double);
                                    }
                                }
                                else if (value is float floatValue)
                                {
                                    // Bezpośrednia reprezentacja jako float
                                    this.value = BitConverter.GetBytes(floatValue);
                                    type = typeof(float);
                                }
                            }
                            else if (value is DateTime)
                            {
                                long dateTimeTicks = ((DateTime)value).Ticks;
                                this.value = BitConverter.GetBytes(dateTimeTicks);
                                type = typeof(DateTime);
                            }
                            else if (value is TimeSpan)
                            {
                                long timeSpanTicks = ((TimeSpan)value).Ticks;
                                this.value = BitConverter.GetBytes(timeSpanTicks);
                                type = typeof(TimeSpan);
                            }
                            else if (value is Guid)
                            {
                                if ((Guid)value == Guid.Empty)
                                    throw new Exception();

                                this.value = ((Guid)value).ToByteArray();
                                type = typeof(Guid);

                            }
                            else if (value is byte)
                            {
                                this.value = new byte[] { value };
                                type = typeof(byte);
                            }
                            else if (value is byte[] || value is List<byte>)
                            {
                                this.value = value;
                                type = typeof(byte[]);
                            }
                            else if (value is int[])
                            {
                                int[] intArray = (int[])value;
                                byte[] bytesIntArray = new byte[intArray.Length * sizeof(int)];
                                Buffer.BlockCopy(intArray, 0, bytesIntArray, 0, bytesIntArray.Length);

                                this.value = bytesIntArray;
                                type = typeof(int[]);
                            }
                            else if (value is long[])
                            {
                                long[] longArray = (long[])value;
                                byte[] bytesLongArray = new byte[longArray.Length * sizeof(long)];
                                Buffer.BlockCopy(longArray, 0, bytesLongArray, 0, bytesLongArray.Length);

                                this.value = bytesLongArray;
                                type = typeof(long[]);
                            }
                            else if (value is double[])
                            {
                                double[] doubleArray = (double[])value;
                                byte[] bytesDoubleArray = new byte[doubleArray.Length * sizeof(double)];
                                Buffer.BlockCopy(doubleArray, 0, bytesDoubleArray, 0, bytesDoubleArray.Length);

                                this.value = bytesDoubleArray;
                                type = typeof(double[]);
                            }
                            else if (value is string[])
                            {
                                string[] stringArray = (string[])value;
                                List<byte> byteList = new List<byte>();

                                foreach (var str in stringArray)
                                {
                                    byte[] stringBytes = Encoding.UTF8.GetBytes(str); // Zakodowanie string do byte[]
                                    int length = stringBytes.Length;

                                    byteList.AddRange(BitConverter.GetBytes(length)); // Zapisanie długości stringu jako int (4 bajty)
                                    byteList.AddRange(stringBytes); // Zapisanie bajtów stringu
                                }

                                this.value = byteList.ToArray();
                                this.type = typeof(string[]);
                            }
                            else
                            {
                                try
                                {
                                    string tryS = value.ToString();
                                    this.value = Encoding.UTF8.GetBytes(tryS);
                                    type = value.GetType();
                                }
                                catch
                                {
                                    type = null;
                                    _parent.ErrorLog("Type of this variable is not supported");
                                    return this;
                                }
                            }

                            _parent.OnValueUpdated();
                        }
                        else
                        {
                            _parent.ErrorLog("Type of this variable is not supported");
                        }
                    }
                    catch
                    {
                        _parent.ErrorLog("Type of this variable is not supported");
                        //Nie poprawny typ
                    }
                }
            }
            return this;
        }

        // Szybkie odczytanie wartości bez boxing – dekoduje według zapisanego this.type, konwertuje do T.
        // Używa MemoryMarshal.Read (brak bounds-check) + Unsafe.As (brak box/unbox) dla typów unmanaged.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T ReadAs<T>()
        {
            var span = value.AsSpan();

            // ── Typy całkowite ────────────────────────────────────────────────
            if (typeof(T) == typeof(int) || typeof(T) == typeof(long) ||
                typeof(T) == typeof(short) || typeof(T) == typeof(byte) ||
                typeof(T) == typeof(uint) || typeof(T) == typeof(ulong) ||
                typeof(T) == typeof(ushort))
            {
                long raw;
                if      (type == typeof(byte))   raw = span[0];
                else if (type == typeof(short))  raw = MemoryMarshal.Read<short>(span);
                else if (type == typeof(ushort)) raw = MemoryMarshal.Read<ushort>(span);
                else if (type == typeof(int))    raw = MemoryMarshal.Read<int>(span);
                else if (type == typeof(uint))   raw = MemoryMarshal.Read<uint>(span);
                else if (type == typeof(long))   raw = MemoryMarshal.Read<long>(span);
                else if (type == typeof(ulong))
                {
                    ulong u = MemoryMarshal.Read<ulong>(span);
                    return Unsafe.As<ulong, T>(ref u);
                }
                else return (T)transformToType(type)!;

                if (typeof(T) == typeof(int))    { int    v = (int)raw;    return Unsafe.As<int,    T>(ref v); }
                if (typeof(T) == typeof(long))   { long   v = raw;         return Unsafe.As<long,   T>(ref v); }
                if (typeof(T) == typeof(short))  { short  v = (short)raw;  return Unsafe.As<short,  T>(ref v); }
                if (typeof(T) == typeof(byte))   { byte   v = (byte)raw;   return Unsafe.As<byte,   T>(ref v); }
                if (typeof(T) == typeof(uint))   { uint   v = (uint)raw;   return Unsafe.As<uint,   T>(ref v); }
                if (typeof(T) == typeof(ulong))  { ulong  v = (ulong)raw;  return Unsafe.As<ulong,  T>(ref v); }
                if (typeof(T) == typeof(ushort)) { ushort v = (ushort)raw; return Unsafe.As<ushort, T>(ref v); }
            }

            // ── Typy zmiennoprzecinkowe ───────────────────────────────────────
            if (typeof(T) == typeof(double))
            {
                double v = type == typeof(float)
                    ? MemoryMarshal.Read<float>(span)
                    : type == typeof(double)
                        ? MemoryMarshal.Read<double>(span)
                        : (double)transformToType(type)!;
                return Unsafe.As<double, T>(ref v);
            }
            if (typeof(T) == typeof(float))
            {
                float v = type == typeof(float)
                    ? MemoryMarshal.Read<float>(span)
                    : type == typeof(double)
                        ? (float)MemoryMarshal.Read<double>(span)
                        : (float)transformToType(type)!;
                return Unsafe.As<float, T>(ref v);
            }

            // ── Typy skalarne / referencyjne ─────────────────────────────────
            if (typeof(T) == typeof(bool))
            {
                bool v = span[0] != 0;
                return Unsafe.As<bool, T>(ref v);
            }
            if (typeof(T) == typeof(decimal))
            {
                decimal v = MemoryMarshal.Read<decimal>(span);
                return Unsafe.As<decimal, T>(ref v);
            }
            if (typeof(T) == typeof(DateTime))
            {
                DateTime v = new DateTime(MemoryMarshal.Read<long>(span));
                return Unsafe.As<DateTime, T>(ref v);
            }
            if (typeof(T) == typeof(TimeSpan))
            {
                TimeSpan v = new TimeSpan(MemoryMarshal.Read<long>(span));
                return Unsafe.As<TimeSpan, T>(ref v);
            }
            if (typeof(T) == typeof(Guid))
            {
                Guid v = MemoryMarshal.Read<Guid>(span);
                return Unsafe.As<Guid, T>(ref v);
            }
            if (typeof(T) == typeof(string))    return (T)(object)Encoding.UTF8.GetString(span);
            if (typeof(T) == typeof(byte[]))
            {
                byte[] result = span.ToArray();
                return (T)(object)result;
            }
            if (typeof(T) == typeof(int[]))
            {
                int[] arr = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, arr, 0, value.Length);
                return (T)(object)arr;
            }
            if (typeof(T) == typeof(long[]))
            {
                long[] arr = new long[value.Length / sizeof(long)];
                Buffer.BlockCopy(value, 0, arr, 0, value.Length);
                return (T)(object)arr;
            }
            if (typeof(T) == typeof(double[]))
            {
                double[] arr = new double[value.Length / sizeof(double)];
                Buffer.BlockCopy(value, 0, arr, 0, value.Length);
                return (T)(object)arr;
            }
            if (typeof(T) == typeof(string[]))
            {
                var list = new List<string>();
                int idx = 0;
                while (idx < value.Length)
                {
                    int len = MemoryMarshal.Read<int>(span.Slice(idx)); idx += 4;
                    list.Add(Encoding.UTF8.GetString(span.Slice(idx, len))); idx += len;
                }
                return (T)(object)list.ToArray();
            }

            // Fallback przez transformToType (typy nieznane)
            return (T)transformToType(type)!;
        }

        private dynamic? transformToType(Type? in_type)
        {
            if (value == null || in_type == null || value.Length == 0)
                return null;

            if (in_type == typeof(bool))
                return BitConverter.ToBoolean(value, 0);
            else if (in_type == typeof(string))
                return Encoding.UTF8.GetString(value);
            else if (in_type == typeof(short))
                return BitConverter.ToInt16(value, 0);
            else if (in_type == typeof(ushort))
                return BitConverter.ToUInt16(value, 0);
            else if (in_type == typeof(int))
                return BitConverter.ToInt32(value, 0);
            else if (in_type == typeof(uint))
                return BitConverter.ToUInt32(value, 0);
            else if (in_type == typeof(long))
                return BitConverter.ToInt64(value, 0);
            else if (in_type == typeof(ulong))
                return BitConverter.ToUInt64(value, 0);
            else if (in_type == typeof(byte))
                return Convert.ToByte(value[0]);
            else if (in_type == typeof(float))
                return BitConverter.ToSingle(value, 0);
            else if (in_type == typeof(double))
                return BitConverter.ToDouble(value, 0);
            else if (in_type == typeof(decimal))
            {
                int[] bits = new int[4];
                Buffer.BlockCopy(value, 0, bits, 0, 16);
                return new decimal(bits);
            }
            else if (in_type == typeof(DateTime))
                return new DateTime(BitConverter.ToInt64(value, 0));
            else if (in_type == typeof(TimeSpan))
                return new TimeSpan(BitConverter.ToInt64(value, 0));
            else if (in_type == typeof(Guid))
                return new Guid(value);
            else if (in_type == null)
                return null;
            else if (in_type == typeof(byte[]))
            {
                byte[] result = new byte[value.Length];
                Array.Copy(value, result, value.Length);

                return result;
            }
            else if (in_type == typeof(int[]))
            {

                int[] liczbyIntOdwrotnie = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, liczbyIntOdwrotnie, 0, value.Length);

                return liczbyIntOdwrotnie;
            }
            else if (in_type == typeof(long[]))
            {
                long[] liczbyLongOdwrotnie = new long[value.Length / sizeof(long)];
                Buffer.BlockCopy(value, 0, liczbyLongOdwrotnie, 0, value.Length);

                return liczbyLongOdwrotnie;
            }
            else if (in_type == typeof(double[]))
            {
                double[] liczbyDoubleOdwrotnie = new double[value.Length / sizeof(double)];
                Buffer.BlockCopy(value, 0, liczbyDoubleOdwrotnie, 0, value.Length);

                return liczbyDoubleOdwrotnie;
            }
            else if (in_type == typeof(string[])) 
            {
                List<string> stringList = new List<string>();
                int index = 0;

                while (index < this.value.Length)
                {
                    int length = BitConverter.ToInt32(this.value, index); // Odczytujemy długość stringu
                    index += sizeof(int);

                    byte[] stringBytes = new byte[length];
                    Array.Copy(this.value, index, stringBytes, 0, length); // Kopiujemy bajty stringu
                    index += length;

                    stringList.Add(Encoding.UTF8.GetString(stringBytes)); // Konwertujemy bajty na string
                }

                return stringList.ToArray();
            }

            throw new Exception("Type is not supported");
        }

        /// <summary>
        /// Returns the value ​​of given type
        /// </summary>
        /// <typeparam name="T">The value ​​will take this type</typeparam>
        /// <returns>The value of the Value object</returns>
        public T Get<T>()
        {
            lock (_engagedElementLock)
            {
                if (value != null && value.Length > 0)
                    return ReadAs<T>();
                return default!;
            }
        }

        /// <summary>
        /// Returns the default value or, if one exists, a stored value with the default value type
        /// </summary>
        /// <param name="defaultOut">Default value, used if no other one is saved</param>
        /// <returns>The value of the Value object</returns>
        public dynamic? Get(dynamic? defaultOut)
        {
            lock (_engagedElementLock)
            {
                if (value != null && value.Length > 0)
                {
                    return transformToType(type);
                }
                return defaultOut;
            }
        }

        /// <summary>
        /// Returns the value obtained automatically
        /// </summary>
        /// <returns>>The value of the Value object</returns>
        public dynamic? Get()
        {
            lock (_engagedElementLock)
            {
                if (value != null && value.Length > 0)
                {
                    return transformToType(type);
                }
                return null;
            }
        }

        public void Drop()
        {
            Container parent;

            lock (_engagedElementLock)
            {
                // zabezpieczenie przed podwójnym Drop()
                if (_parent == null)
                    return;

                parent = _parent;

                value = null;
                type = null;

                _parent = null; // zerwij wcześnie
            }

            // USUWANIE POZA LOCKIEM (ważne)
            parent.ValuesList.Remove(ValueName);
            parent.GlobalValuesSet.Remove(this);
            parent.OnValueUpdated();
        }

        protected internal void GetBytes(System.IO.MemoryStream indexes, System.IO.MemoryStream data)
        {
            lock (_engagedElementLock)
            {
                if (value == null || value.Length == 0 || Type == null || !_myProperties.ListOfTypes.TryGetValue(Type, out var typeByte))
                    return;

                byte[] nameBytes = Encoding.UTF8.GetBytes(ValueName);
                int nameLength = nameBytes.Length;
                if (nameLength >= 32768)
                    throw new ArgumentException("Value name is too long");

                // INDEX – flaga Value w najwyższym bicie
                indexes.Write(BitConverter.GetBytes((ushort)(nameLength + 32768)));
                indexes.Write(nameBytes);
                indexes.WriteByte(typeByte);

                // Pozycja danych zanim zapiszemy nową wartość
                uint dataOffset = (uint)data.Length;
                uint dataSize   = (uint)(value.Length + 1); // +1 za bajt nagłówkowy

                indexes.Write(BitConverter.GetBytes(dataOffset));
                indexes.Write(BitConverter.GetBytes(dataSize));

                // DATA
                data.WriteByte(0); // bajt nagłówkowy [nieużywany]
                data.Write(value, 0, value.Length);
            }
        }
    }
}
