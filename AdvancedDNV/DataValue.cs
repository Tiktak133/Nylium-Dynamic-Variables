using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// Pojedyncza nazwana wartość w kontenerze DNV.
    /// W pamięci trzymamy dokładnie to, co trafia do pliku: kod typu (1 bajt) i bajty danych.
    /// Set zamienia wartość na bajty, Get dekoduje bajty i w razie potrzeby konwertuje typ (patrz <see cref="ValueCodec"/>).
    /// </summary>
    public class Value
    {
        public string ValueName { get; }

        /// <summary> Typ zapisanej wartości (null, gdy nic nie zapisano) </summary>
        public Type? Type { get { lock (_sync) return DNVProperties.ClrType(_code); } }

        private Container? _parent;
        private readonly Lock _sync; // wspólna blokada całego drzewa (Container.SyncRoot)

        // Zawartość: _data[_offset .. _offset + _length]. Tablica nie jest nigdy zmieniana w miejscu — każdy Set podstawia
        // nową — więc odczyt bierze migawkę pod blokadą i dekoduje już poza nią. Po parsowaniu _data to CAŁY bufor
        // ramki/pliku (wartości wskazują na swoje fragmenty, bez kopiowania). Wyjątek: Set(byte[]) trzyma tablicę wywołującego.
        private DNVType _code;
        private byte[]? _data;
        private int _offset;
        private int _length; // Set(byte[], int) może podać większy bufor z ArrayPool

        /// <summary> Długość nazwy w UTF-8 — potrzebna do wyliczenia rozmiaru przy zapisie </summary>
        internal readonly int NameByteCount;

        internal Value(string name, Container parent) : this(name, Encoding.UTF8.GetByteCount(name), parent) { }

        internal Value(string name, int nameByteCount, Container parent)
        {
            if (nameByteCount >= 32768)
                throw new ArgumentException("Value name is too long");

            ValueName     = name;
            NameByteCount = nameByteCount;
            _parent       = parent;
            _sync         = parent.SyncRoot;
        }

        /// <summary> Czy wartość jest ustawiona </summary>
        public bool IsSet()
        {
            lock (_sync)
                return _parent != null && _code != DNVType.None;
        }

        // ── Zapis ───────────────────────────────────────────────────────────────────────────

        public Value Set(bool value)     => Store(DNVType.Bool, SingleByte[value ? 1 : 0]);
        public Value Set(byte value)     => Store(DNVType.Byte, SingleByte[value]);
        public Value Set(short value)    => SetInteger(value);
        public Value Set(ushort value)   => SetInteger(value);
        public Value Set(int value)      => SetInteger(value);
        public Value Set(uint value)     => SetInteger(value);
        public Value Set(long value)     => SetInteger(value);
        public Value Set(ulong value)    => value <= long.MaxValue ? SetInteger((long)value) : StoreFixed(DNVType.ULong, value);
        public Value Set(float value)    => StoreFixed(DNVType.Float, value);
        public Value Set(DateTime value) => StoreFixed(DNVType.DateTime, value.Ticks);
        public Value Set(TimeSpan value) => StoreFixed(DNVType.TimeSpan, value.Ticks);

        /// <summary> double, który da się dokładnie zapisać jako float, zajmuje 4 bajty zamiast 8 </summary>
        public Value Set(double value) =>
            (double)(float)value == value ? StoreFixed(DNVType.Float, (float)value) : StoreFixed(DNVType.Double, value);

        /// <summary> decimal zapisujemy najkrócej, jak się da dokładnie: float, double albo pełne 16 bajtów </summary>
        public Value Set(decimal value)
        {
            float f = (float)value;
            if (Math.Abs(f) < 7.9e28f && (decimal)f == value)
                return StoreFixed(DNVType.Float, f);

            double d = (double)value;
            if (Math.Abs(d) < 7.9e28 && (decimal)d == value)
                return StoreFixed(DNVType.Double, d);

            return Store(DNVType.Decimal, ValueCodec.WriteDecimal(value));
        }

        public Value Set(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("Guid cannot be empty");
            return Store(DNVType.Guid, value.ToByteArray());
        }

        public Value Set(string? value) => value == null ? Clear() : Store(DNVType.String, Encoding.UTF8.GetBytes(value));

        /// <summary> Zapamiętuje REFERENCJĘ do tablicy (bez kopiowania) </summary>
        public Value Set(byte[]? value) => value == null ? Clear() : Store(DNVType.ByteArray, value);

        /// <summary>
        /// Jak <see cref="Set(byte[])"/>, ale zapisuje tylko pierwsze <paramref name="valueLen"/> bajtów tablicy.
        /// KLUCZOWE dla buforów z ArrayPool (chunki plików w cloud): pool wydaje kubełki o rozmiarach potęg dwójki,
        /// więc np. chunk 2,5 MB siedzi w buforze 4 MiB. Zwykłe Set(byte[]) wysłałoby cały kubełek ze śmieciowym ogonem.
        /// </summary>
        public Value Set(byte[] value, int valueLen)
        {
            if ((uint)valueLen > (uint)value.Length)
                throw new ArgumentOutOfRangeException(nameof(valueLen), $"valueLen ({valueLen}) poza zakresem tablicy ({value.Length}).");
            return Store(DNVType.ByteArray, value, valueLen);
        }

        public Value Set(int[]? value)    => value == null ? Clear() : Store(DNVType.IntArray, MemoryMarshal.AsBytes(value.AsSpan()).ToArray());
        public Value Set(long[]? value)   => value == null ? Clear() : Store(DNVType.LongArray, MemoryMarshal.AsBytes(value.AsSpan()).ToArray());
        public Value Set(double[]? value) => value == null ? Clear() : Store(DNVType.DoubleArray, MemoryMarshal.AsBytes(value.AsSpan()).ToArray());
        public Value Set(string[]? value) => value == null ? Clear() : Store(DNVType.StringArray, ValueCodec.WriteStringArray(value));

        /// <summary>
        /// Zapis wartości znanej dopiero w czasie działania (np. z <c>dynamic</c> albo <c>object</c>).
        /// Enumy zapisują się jako nazwa, nieznane typy — jako tekst (ToString). null czyści wartość.
        /// </summary>
        public Value Set(object? value) => value switch
        {
            null           => Clear(),
            bool v         => Set(v),
            byte v         => Set(v),
            sbyte v        => SetInteger(v),
            short v        => SetInteger(v),
            ushort v       => SetInteger(v),
            char v         => SetInteger(v),
            int v          => SetInteger(v),
            uint v         => SetInteger(v),
            long v         => SetInteger(v),
            ulong v        => Set(v),
            float v        => Set(v),
            double v       => Set(v),
            decimal v      => Set(v),
            string v       => Set(v),
            Guid v         => Set(v),
            DateTime v     => Set(v),
            TimeSpan v     => Set(v),
            byte[] v       => Set(v),
            int[] v        => Set(v),
            long[] v       => Set(v),
            double[] v     => Set(v),
            string[] v     => Set(v),
            List<byte> v   => Set(v.ToArray()),
            List<int> v    => Set(v.ToArray()),
            List<long> v   => Set(v.ToArray()),
            List<double> v => Set(v.ToArray()),
            List<string> v => Set(v.ToArray()),
            Enum v         => Set(v.ToString()),
            _              => Set(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };

        /// <summary> Liczby całkowite zapisujemy w najmniejszym typie, który je mieści: byte, short, int albo long </summary>
        private Value SetInteger(long v)
        {
            if (v >= 0 && v <= byte.MaxValue)               return Store(DNVType.Byte, SingleByte[v]);
            if (v >= short.MinValue && v <= short.MaxValue) return StoreFixed(DNVType.Short, (short)v);
            if (v >= int.MinValue && v <= int.MaxValue)     return StoreFixed(DNVType.Int, (int)v);
            return StoreFixed(DNVType.Long, v);
        }

        /// <summary>
        /// Wspólne jednobajtowe tablice dla bool i liczb 0-255 — najczęstszych wartości w ramkach (flagi, kody, małe liczniki).
        /// Ustawienie takiej wartości nic nie alokuje. Bezpieczne, bo zawartości wartości nigdy nie zmieniamy w miejscu,
        /// a odczyt zawsze zwraca kopię.
        /// </summary>
        private static readonly byte[][] SingleByte = Enumerable.Range(0, 256).Select(i => new[] { (byte)i }).ToArray();

        private Value StoreFixed<T>(DNVType code, T value) where T : unmanaged
        {
            byte[] bytes = new byte[Unsafe.SizeOf<T>()];
            MemoryMarshal.Write(bytes, in value);
            return Store(code, bytes);
        }

        private Value Clear() => Store(DNVType.None, null, 0);

        private Value Store(DNVType code, byte[]? data) => Store(code, data, data?.Length ?? 0);

        private Value Store(DNVType code, byte[]? data, int length)
        {
            Container? parent;
            lock (_sync)
            {
                _code   = data == null ? DNVType.None : code;
                _data   = data;
                _offset = 0;
                _length = data == null ? 0 : length;
                parent  = _parent;
            }
            parent?.OnValueUpdated();
            return this;
        }

        // ── Dodawanie ───────────────────────────────────────────────────────────────────────

        /// <summary> Dodaje liczbę do zapisanej wartości (pusta wartość = 0; ułamek pozostaje ułamkiem; tekst dostaje dopisaną liczbę) </summary>
        public Value Add(long addend)
        {
            lock (_sync)
            {
                object? current = DecodeCurrent();
                return current switch
                {
                    null                       => SetInteger(addend),
                    string s                   => Set(s + addend.ToString(CultureInfo.InvariantCulture)),
                    float or double or decimal => Set(Convert.ToDouble(current, CultureInfo.InvariantCulture) + addend),
                    _                          => SetInteger(Convert.ToInt64(current, CultureInfo.InvariantCulture) + addend),
                };
            }
        }

        /// <summary> Dodaje ułamek do zapisanej liczby (pusta wartość = 0; tekst dostaje dopisaną liczbę) </summary>
        public Value Add(double addend)
        {
            lock (_sync)
            {
                return DecodeCurrent() switch
                {
                    null        => Set(addend),
                    string s    => Set(s + addend.ToString(CultureInfo.InvariantCulture)),
                    var current => Set(Convert.ToDouble(current, CultureInfo.InvariantCulture) + addend),
                };
            }
        }

        /// <summary> Dopisuje tekst na końcu zapisanej wartości </summary>
        public Value Add(string addend)
        {
            lock (_sync)
            {
                object? current = DecodeCurrent();
                return Set(Convert.ToString(current, CultureInfo.InvariantCulture) + addend);
            }
        }

        /// <summary> Dodawanie wartości znanej dopiero w czasie działania. Wybiera Add(long), Add(double) albo Add(string). </summary>
        public Value Add(object? value) => value switch
        {
            null                        => this,
            bool v                      => Set(v),
            string v                    => Add(v),
            float or double or decimal  => Add(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            sbyte or byte or short or ushort or int or uint or long or ulong
                                        => Add(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            _                           => Set(value),
        };

        private object? DecodeCurrent() =>
            _code == DNVType.None ? null : ValueCodec.Decode(_code, _data.AsSpan(_offset, _length));

        // ── Odczyt ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Wartość jako <typeparamref name="T"/>, a gdy nic nie zapisano — default(T).
        /// Liczby konwertują się między sobą (ułamki są ucinane), tekst na liczby i odwrotnie, tablice na listy.
        /// </summary>
        /// <exception cref="InvalidCastException">Zapisanej wartości nie da się przedstawić jako T</exception>
        public T Get<T>() => TryGetContent(out var code, out var bytes) ? ValueCodec.Read<T>(code, bytes, ValueName) : default!;

        /// <summary> Jak <see cref="Get{T}()"/>, ale gdy nic nie zapisano — <paramref name="defaultOut"/> </summary>
        public T Get<T>(T defaultOut) => TryGetContent(out var code, out var bytes) ? ValueCodec.Read<T>(code, bytes, ValueName) : defaultOut;

        /// <summary> Wartość w typie, w jakim ją zapisano, a gdy nic nie zapisano — <paramref name="defaultOut"/> </summary>
        public dynamic? Get(dynamic? defaultOut) => TryGetContent(out var code, out var bytes) ? ValueCodec.Decode(code, bytes) : defaultOut;

        /// <summary> Wartość w typie, w jakim ją zapisano, a gdy nic nie zapisano — null </summary>
        public dynamic? Get() => TryGetContent(out var code, out var bytes) ? ValueCodec.Decode(code, bytes) : null;

        private bool TryGetContent(out DNVType code, out ReadOnlySpan<byte> bytes)
        {
            lock (_sync)
            {
                code  = _code;
                bytes = _data.AsSpan(_offset, _length);
                return code != DNVType.None;
            }
        }

        // ── Usuwanie, kopiowanie, serializacja ──────────────────────────────────────────────

        /// <summary> Usuwa wartość z kontenera </summary>
        public void Drop()
        {
            Container? parent;
            lock (_sync)
            {
                parent = _parent;
                if (parent == null)
                    return; // już usunięta

                if (parent.ValuesList.TryGetValue(ValueName, out var registered) && registered == this)
                    parent.ValuesList.Remove(ValueName);

                _parent = null;
                _code   = DNVType.None;
                _data   = null;
                _offset = 0;
                _length = 0;
            }
            parent.OnValueUpdated();
        }

        /// <summary> Kopiuje zawartość do innej wartości (np. przy wyodrębnianiu do ramki) </summary>
        internal void CopyTo(Value target)
        {
            if (TryGetContent(out var code, out var bytes))
                target.Store(code, bytes.ToArray());
        }

        /// <summary>
        /// Wczytanie zawartości przy parsowaniu pliku/ramki (bez zdarzenia zmiany). Wartość wskazuje na fragment
        /// bufora — bez kopiowania; bufor nie może być potem zmieniany (patrz konstruktor DNVFrame).
        /// </summary>
        internal void Load(DNVType code, byte[] buffer, int offset, int length)
        {
            _code   = code;
            _data   = buffer;
            _offset = offset;
            _length = length;
        }

        // ── Serializacja (wywoływana pod blokadą drzewa) ────────────────────────────────────
        // Indeks: [2B długość nazwy | 0x8000][nazwa UTF-8][1B kod typu][4B przesunięcie w danych][4B rozmiar + 1]
        // Dane:   [1B nagłówka, zawsze 0][bajty wartości]

        /// <summary>
        /// Czy wartość trafia do zapisu. Pustych wartości (brak, "" albo pusta tablica) NIE wysyłamy — tak robiły wszystkie
        /// wcześniejsze wersje, a serwer na starszej bibliotece rozpoznaje brak pola po IsSet(). Wysłanie "" zmieniłoby jego logikę.
        /// </summary>
        internal bool IsWritten  => _code != DNVType.None && _length > 0;
        internal int  IndexSize  => 2 + NameByteCount + 1 + 4 + 4;
        internal int  DataSize   => 1 + _length;

        /// <summary> Zapis wprost do docelowego bufora (rozmiary wyliczone wcześniej przez IndexSize/DataSize) </summary>
        internal void WriteTo(Span<byte> index, ref int indexPos, Span<byte> data, ref int dataPos)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(index[indexPos..], (ushort)(NameByteCount | Container.ValueFlag));
            indexPos += 2;
            indexPos += Encoding.UTF8.GetBytes(ValueName, index[indexPos..]);
            index[indexPos++] = (byte)_code;
            BinaryPrimitives.WriteUInt32LittleEndian(index[indexPos..], (uint)dataPos);
            BinaryPrimitives.WriteUInt32LittleEndian(index[(indexPos + 4)..], (uint)(_length + 1));
            indexPos += 8;

            data[dataPos++] = 0;
            _data.AsSpan(_offset, _length).CopyTo(data[dataPos..]);
            dataPos += _length;
        }
    }
}
