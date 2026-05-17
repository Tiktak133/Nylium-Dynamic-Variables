using System.Buffers;
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
        // Jeden bufor 16B na wszystkie typy unmanaged (bool/byte/short/int/long/float/double/Guid/DateTime/TimeSpan)
        // Zamiast 5 osobnych byte[] per Value → 1 alokacja. _valueLen opisuje ile bajtów jest aktualnie użytych.
        private readonly byte[] _buf16 = new byte[16];
        internal int _valueLen = 0; // aktualny rozmiar value (używany gdy value == _buf16)

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
        /// Adds a numeric value to the currently stored integer value.
        /// </summary>
        public Value Add(long addend)
        {
            lock (_engagedElementLock)
            {
                if (_parent == null) return this;
                long current = _valueLen > 0 ? Get<long>() : 0L;
                Set(current + addend);
            }
            return this;
        }

        /// <summary>
        /// Appends a string to the currently stored string value.
        /// </summary>
        public Value Add(string addend)
        {
            lock (_engagedElementLock)
            {
                if (_parent == null) return this;
                string current = _valueLen > 0 ? (Get<string>() ?? string.Empty) : string.Empty;
                Set(current + addend);
            }
            return this;
        }

        /// <summary>
        /// Adds a floating-point value to the currently stored numeric value.
        /// </summary>
        public Value Add(double addend)
        {
            lock (_engagedElementLock)
            {
                if (_parent == null) return this;
                double current = _valueLen > 0 ? Get<double>() : 0.0;
                Set(current + addend);
            }
            return this;
        }

        /// <summary>
        /// Routes to typed Add based on runtime type. Prefer typed overloads (Add(long), Add(string), Add(double)).
        /// </summary>
        public Value Add(object? value)
        {
            if (value == null) return this;
            return value switch
            {
                bool   v => Set(v),
                byte   v => Add((long)v),
                short  v => Add((long)v),
                ushort v => Add((long)v),
                int    v => Add((long)v),
                uint   v => Add((long)v),
                long   v => Add(v),
                ulong  v => Add((long)v),
                float  v => Add((double)v),
                double v => Add(v),
                string v => Add(v),
                _        => Set(value),
            };
        }

        /// <summary>
        // ── Typowane przeciążenia Set

        public Value Set(bool value)
        {
            lock (_engagedElementLock)
            {
                _buf16[0] = value ? (byte)1 : (byte)0;
                this.value = _buf16; _valueLen = 1;
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
                    _buf16[0] = (byte)value;
                    this.value = _buf16; _valueLen = 1;
                    type = typeof(byte);
                }
                else if (value >= short.MinValue && value <= short.MaxValue)
                {
                    MemoryMarshal.Write(_buf16, (short)value);
                    this.value = _buf16; _valueLen = 2;
                    type = typeof(short);
                }
                else
                {
                    MemoryMarshal.Write(_buf16, value);
                    this.value = _buf16; _valueLen = 4;
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
                    _buf16[0] = (byte)value;
                    this.value = _buf16; _valueLen = 1;
                    type = typeof(byte);
                }
                else if (value >= short.MinValue && value <= short.MaxValue)
                {
                    MemoryMarshal.Write(_buf16, (short)value);
                    this.value = _buf16; _valueLen = 2;
                    type = typeof(short);
                }
                else if (value >= int.MinValue && value <= int.MaxValue)
                {
                    MemoryMarshal.Write(_buf16, (int)value);
                    this.value = _buf16; _valueLen = 4;
                    type = typeof(int);
                }
                else
                {
                    MemoryMarshal.Write(_buf16, value);
                    this.value = _buf16; _valueLen = 8;
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
                    MemoryMarshal.Write(_buf16, (float)value);
                    this.value = _buf16; _valueLen = 4;
                    type = typeof(float);
                }
                else
                {
                    MemoryMarshal.Write(_buf16, value);
                    this.value = _buf16; _valueLen = 8;
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
                MemoryMarshal.Write(_buf16, value);
                this.value = _buf16; _valueLen = 4;
                type = typeof(float);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(string value)
        {
            if (value == null)
            {
                lock (_engagedElementLock) { this.value = null; type = null; _valueLen = 0; }
                return this;
            }
            lock (_engagedElementLock)
            {
                this.value = Encoding.UTF8.GetBytes(value);
                _valueLen = this.value.Length;
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
                value.TryWriteBytes(_buf16);
                this.value = _buf16; _valueLen = 16;
                type = typeof(Guid);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(DateTime value)
        {
            lock (_engagedElementLock)
            {
                MemoryMarshal.Write(_buf16, value.Ticks);
                this.value = _buf16; _valueLen = 8;
                type = typeof(DateTime);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(TimeSpan value)
        {
            lock (_engagedElementLock)
            {
                MemoryMarshal.Write(_buf16, value.Ticks);
                this.value = _buf16; _valueLen = 8;
                type = typeof(TimeSpan);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        public Value Set(byte value)
        {
            lock (_engagedElementLock)
            {
                _buf16[0] = value;
                this.value = _buf16; _valueLen = 1;
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
                _valueLen = value.Length;
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
                _valueLen = bytes.Length;
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
                _valueLen = bytes.Length;
                type = typeof(double[]);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        // ── Fallback Set(object?) — router dla nieznanych typów lub legacy wywołań ─

        /// <summary>
        /// Routes to the appropriate typed Set overload based on the runtime type of <paramref name="value"/>.
        /// Prefer typed overloads (Set(int), Set(string), …) for zero-overhead dispatch.
        /// </summary>
        public Value Set(object? value)
        {
            if (value == null)
            {
                lock (_engagedElementLock) { this.value = null; type = null; _valueLen = 0; }
                return this;
            }

            return value switch
            {
                bool   v => Set(v),
                byte   v => Set(v),
                short  v => Set((int)v),
                ushort v => Set((int)v),
                int    v => Set(v),
                uint   v => Set((long)v),
                long   v => Set(v),
                ulong  v => Set((long)v),
                float  v => Set(v),
                double v => Set(v),
                decimal v => SetDecimal(v),
                string v => Set(v),
                Guid   v => Set(v),
                DateTime v => Set(v),
                TimeSpan v => Set(v),
                byte[] v => Set(v),
                int[]  v => Set(v),
                double[] v => Set(v),
                long[] v => SetLongArray(v),
                string[] v => SetStringArray(v),
                List<int>    v => Set(v.ToArray()),
                List<long>   v => Set(v.ToArray()),
                List<double> v => Set(v.ToArray()),
                List<string> v => SetStringArray(v.ToArray()),
                _ => SetFallback(value),
            };
        }

        private Value SetDecimal(decimal v)
        {
            lock (_engagedElementLock)
            {
                if ((decimal)(float)v == v)       return Set((float)v);
                if ((decimal)(double)v == v)      return Set((double)v);
                int[] bits = decimal.GetBits(v);
                var bytes = new byte[16];
                Buffer.BlockCopy(bits, 0, bytes, 0, 16);
                this.value = bytes; _valueLen = 16; type = typeof(decimal);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        private Value SetLongArray(long[] v)
        {
            lock (_engagedElementLock)
            {
                var bytes = new byte[v.Length * sizeof(long)];
                Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
                this.value = bytes; _valueLen = bytes.Length; type = typeof(long[]);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        private Value SetStringArray(string[] v)
        {
            using var ms = new System.IO.MemoryStream();
            Span<byte> lenBuf = stackalloc byte[4];
            foreach (var s in v)
            {
                byte[] sb = Encoding.UTF8.GetBytes(s);
                MemoryMarshal.Write(lenBuf, sb.Length);
                ms.Write(lenBuf);
                ms.Write(sb);
            }
            lock (_engagedElementLock)
            {
                this.value = ms.ToArray(); _valueLen = (int)ms.Length; type = typeof(string[]);
                _parent?.OnValueUpdated();
            }
            return this;
        }

        private Value SetFallback(object value)
        {
            lock (_engagedElementLock)
            {
                string s = value.ToString() ?? string.Empty;
                this.value = Encoding.UTF8.GetBytes(s);
                _valueLen = this.value.Length;
                type = value.GetType();
                _parent?.OnValueUpdated();
            }
            return this;
        }

        // Szybkie odczytanie wartości bez boxing
        // Używa MemoryMarshal.Read (brak bounds-check) + Unsafe.As (brak box/unbox) dla typów unmanaged.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T ReadAs<T>()
        {
            var span = value.AsSpan(0, _valueLen);

            // ── Nullable<TInner> — odpakuj typ wewnętrzny, odczytaj, zapakuj z powrotem ─
            var tType = typeof(T);
            if (tType.IsGenericType && tType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var inner = Nullable.GetUnderlyingType(tType)!;
                // Wywołaj ReadAs dla typu wewnętrznego przez refleksję (raz per unikalna T, JIT cache)
                object innerValue = ReadAsBoxed(inner, span);
                return (T)innerValue;
            }

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
                int[] arr = new int[_valueLen / sizeof(int)];
                Buffer.BlockCopy(value, 0, arr, 0, _valueLen);
                return (T)(object)arr;
            }
            if (typeof(T) == typeof(long[]))
            {
                long[] arr = new long[_valueLen / sizeof(long)];
                Buffer.BlockCopy(value, 0, arr, 0, _valueLen);
                return (T)(object)arr;
            }
            if (typeof(T) == typeof(double[]))
            {
                double[] arr = new double[_valueLen / sizeof(double)];
                Buffer.BlockCopy(value, 0, arr, 0, _valueLen);
                return (T)(object)arr;
            }
            if (typeof(T) == typeof(string[]))
            {
                var list = new List<string>();
                int idx = 0;
                while (idx < _valueLen)
                {
                    int len = MemoryMarshal.Read<int>(span.Slice(idx)); idx += 4;
                    list.Add(Encoding.UTF8.GetString(span.Slice(idx, len))); idx += len;
                }
                return (T)(object)list.ToArray();
            }

            // ── List<T> — przechowywane jako odpowiednia tablica ──────────────
            if (typeof(T) == typeof(List<int>))
            {
                int[] arr = new int[_valueLen / sizeof(int)];
                Buffer.BlockCopy(value, 0, arr, 0, _valueLen);
                return (T)(object)arr.ToList();
            }
            if (typeof(T) == typeof(List<long>))
            {
                long[] arr = new long[_valueLen / sizeof(long)];
                Buffer.BlockCopy(value, 0, arr, 0, _valueLen);
                return (T)(object)arr.ToList();
            }
            if (typeof(T) == typeof(List<double>))
            {
                double[] arr = new double[_valueLen / sizeof(double)];
                Buffer.BlockCopy(value, 0, arr, 0, _valueLen);
                return (T)(object)arr.ToList();
            }
            if (typeof(T) == typeof(List<string>))
            {
                var list = new List<string>();
                int idx = 0;
                while (idx < _valueLen)
                {
                    int len = MemoryMarshal.Read<int>(span.Slice(idx)); idx += 4;
                    list.Add(Encoding.UTF8.GetString(span.Slice(idx, len))); idx += len;
                }
                return (T)(object)list;
            }

            // Fallback przez transformToType (typy nieznane)
            return (T)transformToType(type)!;
        }

        // Odczyt wartości jako boxed object dla podanego Type — używany przez ścieżkę Nullable<T>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object ReadAsBoxed(Type inner, ReadOnlySpan<byte> span)
        {
            if (inner == typeof(int))       return (object)ReadIntegerAs<int>(span);
            if (inner == typeof(long))      return (object)ReadIntegerAs<long>(span);
            if (inner == typeof(short))     return (object)ReadIntegerAs<short>(span);
            if (inner == typeof(byte))      return (object)ReadIntegerAs<byte>(span);
            if (inner == typeof(uint))      return (object)ReadIntegerAs<uint>(span);
            if (inner == typeof(ulong))     return (object)ReadIntegerAs<ulong>(span);
            if (inner == typeof(ushort))    return (object)ReadIntegerAs<ushort>(span);
            if (inner == typeof(float))
            {
                // Wartość może być przechowana jako float, double lub typ całkowity
                if (type == typeof(float))  return (object)MemoryMarshal.Read<float>(span);
                if (type == typeof(double)) return (object)(float)MemoryMarshal.Read<double>(span);
                return (object)(float)Convert.ToDouble(transformToType(type));
            }
            if (inner == typeof(double))
            {
                if (type == typeof(float))  return (object)(double)MemoryMarshal.Read<float>(span);
                if (type == typeof(double)) return (object)MemoryMarshal.Read<double>(span);
                return (object)Convert.ToDouble(transformToType(type));
            }
            if (inner == typeof(bool))      return (object)(span[0] != 0);
            if (inner == typeof(decimal))   return (object)MemoryMarshal.Read<decimal>(span);
            if (inner == typeof(DateTime))  return (object)new DateTime(MemoryMarshal.Read<long>(span));
            if (inner == typeof(TimeSpan))  return (object)new TimeSpan(MemoryMarshal.Read<long>(span));
            if (inner == typeof(Guid))      return (object)MemoryMarshal.Read<Guid>(span);
            return transformToType(inner) ?? throw new NotSupportedException($"Nullable<{inner.Name}> is not supported.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TInt ReadIntegerAs<TInt>(ReadOnlySpan<byte> span) where TInt : struct
        {
            long raw;
            if      (type == typeof(byte))   raw = span[0];
            else if (type == typeof(short))  raw = MemoryMarshal.Read<short>(span);
            else if (type == typeof(ushort)) raw = MemoryMarshal.Read<ushort>(span);
            else if (type == typeof(int))    raw = MemoryMarshal.Read<int>(span);
            else if (type == typeof(uint))   raw = MemoryMarshal.Read<uint>(span);
            else if (type == typeof(long))   raw = MemoryMarshal.Read<long>(span);
            else if (type == typeof(ulong))  { ulong u = MemoryMarshal.Read<ulong>(span); return Unsafe.As<ulong, TInt>(ref u); }
            else                             raw = Convert.ToInt64(transformToType(type));

            if (typeof(TInt) == typeof(int))    { int    v = (int)raw;    return Unsafe.As<int,    TInt>(ref v); }
            if (typeof(TInt) == typeof(long))   { long   v = raw;         return Unsafe.As<long,   TInt>(ref v); }
            if (typeof(TInt) == typeof(short))  { short  v = (short)raw;  return Unsafe.As<short,  TInt>(ref v); }
            if (typeof(TInt) == typeof(byte))   { byte   v = (byte)raw;   return Unsafe.As<byte,   TInt>(ref v); }
            if (typeof(TInt) == typeof(uint))   { uint   v = (uint)raw;   return Unsafe.As<uint,   TInt>(ref v); }
            if (typeof(TInt) == typeof(ulong))  { ulong  v = (ulong)raw;  return Unsafe.As<ulong,  TInt>(ref v); }
            if (typeof(TInt) == typeof(ushort)) { ushort v = (ushort)raw; return Unsafe.As<ushort, TInt>(ref v); }
            return default;
        }

        private object? transformToType(Type? in_type)
        {
            if (value == null || in_type == null || _valueLen == 0)
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
            else if (in_type == typeof(List<int>))
            {
                int[] arr = new int[value.Length / sizeof(int)];
                Buffer.BlockCopy(value, 0, arr, 0, value.Length);
                return arr.ToList();
            }
            else if (in_type == typeof(List<long>))
            {
                long[] arr = new long[value.Length / sizeof(long)];
                Buffer.BlockCopy(value, 0, arr, 0, value.Length);
                return arr.ToList();
            }
            else if (in_type == typeof(List<double>))
            {
                double[] arr = new double[value.Length / sizeof(double)];
                Buffer.BlockCopy(value, 0, arr, 0, value.Length);
                return arr.ToList();
            }
            else if (in_type == typeof(List<string>))
            {
                var list = new List<string>();
                int index = 0;
                while (index < this.value.Length)
                {
                    int length = BitConverter.ToInt32(this.value, index);
                    index += sizeof(int);
                    byte[] stringBytes = new byte[length];
                    Array.Copy(this.value, index, stringBytes, 0, length);
                    index += length;
                    list.Add(Encoding.UTF8.GetString(stringBytes));
                }
                return list;
            }

            throw new NotSupportedException($"Type '{in_type.Name}' is not supported by Value.Get<T>().");
        }

        /// <summary>
        /// Returns the value of given type, or <paramref name="defaultOut"/> if no value is stored.
        /// </summary>
        /// <typeparam name="T">The value will take this type</typeparam>
        /// <param name="defaultOut">Value returned when nothing is stored in this object</param>
        /// <returns>The stored value cast to <typeparamref name="T"/>, or <paramref name="defaultOut"/></returns>
        public T Get<T>(T defaultOut)
        {
            lock (_engagedElementLock)
            {
                if (value != null && _valueLen > 0)
                    return ReadAs<T>();
                return defaultOut;
            }
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
                if (value != null && _valueLen > 0)
                    return ReadAs<T>();
                // Dla Nullable<TInner> zwróć null zamiast default wartości struktury
                var tType = typeof(T);
                if (tType.IsGenericType && tType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    return default!; // null dla Nullable<T>
                return default!;
            }
        }

        /// <summary>
        /// Returns the default value or, if one exists, a stored value with the default value type.
        /// </summary>
        /// <param name="defaultOut">Default value, used if no other one is saved</param>
        /// <returns>The value of the Value object</returns>
        /// <remarks>
        /// <b>Migration note:</b> Replace with the typed <c>Get&lt;T&gt;()</c> overload to eliminate
        /// DLR overhead and boxing. Example: <c>value.Get&lt;int&gt;()</c> instead of <c>(int)value.Get(0)</c>.
        /// This overload is kept for backward compatibility only.
        /// </remarks>
        [Obsolete("Use Get<T>() for zero-overhead typed access. This overload uses DLR and causes boxing.")]
        public dynamic? Get(dynamic? defaultOut)
        {
            lock (_engagedElementLock)
            {
                if (value == null || _valueLen == 0) return defaultOut;
                return DecodeValue() ?? defaultOut;
            }
        }
        /// <summary>
        /// Returns the value obtained automatically.
        /// </summary>
        /// <returns>The value of the Value object</returns>
        /// <remarks>
        /// <b>Migration note:</b> Replace with the typed <c>Get&lt;T&gt;()</c> overload to eliminate
        /// DLR overhead and boxing. Example: <c>value.Get&lt;int&gt;()</c> instead of <c>(int)value.Get()</c>.
        /// This overload is kept for backward compatibility only.
        /// </remarks>
        [Obsolete("Use Get<T>() for zero-overhead typed access. This overload uses DLR and causes boxing.")]
        public dynamic? Get()
        {
            lock (_engagedElementLock)
            {
                if (value == null || _valueLen == 0) return null;
                return DecodeValue();
            }
        }

        // Szybkie dekodowanie bez DLR — switch po type eliminuje boxing przez zwrot object
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private object? DecodeValue()
        {
            var span = value.AsSpan(0, _valueLen);
            if (type == typeof(int))       return (int)   MemoryMarshal.Read<int>(span);
            if (type == typeof(long))      return (long)  MemoryMarshal.Read<long>(span);
            if (type == typeof(bool))      return span[0] != 0;
            if (type == typeof(string))    return Encoding.UTF8.GetString(span);
            if (type == typeof(double))    return (double)MemoryMarshal.Read<double>(span);
            if (type == typeof(float))     return (float) MemoryMarshal.Read<float>(span);
            if (type == typeof(short))     return (short) MemoryMarshal.Read<short>(span);
            if (type == typeof(byte))      return span[0];
            if (type == typeof(Guid))      return MemoryMarshal.Read<Guid>(span);
            if (type == typeof(DateTime))  return new DateTime(MemoryMarshal.Read<long>(span));
            if (type == typeof(TimeSpan))  return new TimeSpan(MemoryMarshal.Read<long>(span));
            if (type == typeof(uint))      return (uint)  MemoryMarshal.Read<uint>(span);
            if (type == typeof(ulong))     return (ulong) MemoryMarshal.Read<ulong>(span);
            if (type == typeof(ushort))    return (ushort)MemoryMarshal.Read<ushort>(span);
            if (type == typeof(decimal))   return MemoryMarshal.Read<decimal>(span);
            if (type == typeof(byte[]))    return span.ToArray();
            if (type == typeof(int[]))     { var a = new int[_valueLen / 4];    Buffer.BlockCopy(value, 0, a, 0, _valueLen); return a; }
            if (type == typeof(long[]))    { var a = new long[_valueLen / 8];   Buffer.BlockCopy(value, 0, a, 0, _valueLen); return a; }
            if (type == typeof(double[]))  { var a = new double[_valueLen / 8]; Buffer.BlockCopy(value, 0, a, 0, _valueLen); return a; }
            if (type == typeof(string[]))  return ReadAs<string[]>();
            return transformToType(type); // fallback dla egzotycznych typów
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
                _valueLen = 0;

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
                if (value == null || _valueLen == 0 || Type == null || !_myProperties.ListOfTypes.TryGetValue(Type, out var typeByte))
                    return;

                // ArrayPool dla nazwy ValueName — zero alokacji na stercie w steady state
                int maxNameBytes = Encoding.UTF8.GetMaxByteCount(ValueName.Length);
                byte[] rentedName = System.Buffers.ArrayPool<byte>.Shared.Rent(maxNameBytes);
                int nameLength;
                try
                {
                    nameLength = Encoding.UTF8.GetBytes(ValueName, 0, ValueName.Length, rentedName, 0);
                    if (nameLength >= 32768)
                        throw new ArgumentException("Value name is too long");

                    // INDEX – flaga Value w najwyższym bicie — stackalloc: zero alokacji na stercie
                    ushort nameFlag = (ushort)(nameLength + 32768);
                    Span<byte> hdr = stackalloc byte[2];
                    hdr[0] = (byte)nameFlag; hdr[1] = (byte)(nameFlag >> 8);
                    indexes.Write(hdr);
                    indexes.Write(rentedName, 0, nameLength);
                    indexes.WriteByte(typeByte);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rentedName);
                }

                // Pozycja danych zanim zapiszemy nową wartość
                uint dataOffset = (uint)data.Length;
                uint dataSize   = (uint)(_valueLen + 1); // +1 za bajt nagłówkowy

                Span<byte> offsetBuf = stackalloc byte[4];
                offsetBuf[0] = (byte)dataOffset;       offsetBuf[1] = (byte)(dataOffset >> 8);
                offsetBuf[2] = (byte)(dataOffset >> 16); offsetBuf[3] = (byte)(dataOffset >> 24);
                indexes.Write(offsetBuf);
                offsetBuf[0] = (byte)dataSize;         offsetBuf[1] = (byte)(dataSize >> 8);
                offsetBuf[2] = (byte)(dataSize >> 16);   offsetBuf[3] = (byte)(dataSize >> 24);
                indexes.Write(offsetBuf);

                // DATA
                data.WriteByte(0); // bajt nagłówkowy [nieużywany]
                data.Write(value, 0, _valueLen);
            }
        }
    }
}
