using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AdvancedDNV
{
    /// <summary>
    /// Zamiana bajtów zapisanych w DNV na obiekty .NET i konwersja na typ, o który prosi wywołujący.
    /// Liczby są zapisywane w kolejności little-endian — tak samo na PC (x64) i telefonach (ARM).
    /// </summary>
    internal static class ValueCodec
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── Bajty -> obiekt zapisanego typu ─────────────────────────────────────────────────

        internal static object Decode(DNVType code, ReadOnlySpan<byte> b) => code switch
        {
            DNVType.Bool        => (object)(b[0] != 0),
            DNVType.Byte        => b[0],
            DNVType.Short       => MemoryMarshal.Read<short>(b),
            DNVType.UShort      => MemoryMarshal.Read<ushort>(b),
            DNVType.Int         => MemoryMarshal.Read<int>(b),
            DNVType.UInt        => MemoryMarshal.Read<uint>(b),
            DNVType.Long        => MemoryMarshal.Read<long>(b),
            DNVType.ULong       => MemoryMarshal.Read<ulong>(b),
            DNVType.Float       => MemoryMarshal.Read<float>(b),
            DNVType.Double      => MemoryMarshal.Read<double>(b),
            DNVType.Decimal     => ReadDecimal(b),
            DNVType.DateTime    => new DateTime(MemoryMarshal.Read<long>(b)),
            DNVType.TimeSpan    => new TimeSpan(MemoryMarshal.Read<long>(b)),
            DNVType.Guid        => new Guid(b[..16]),
            DNVType.String      => Encoding.UTF8.GetString(b),
            DNVType.ByteArray   => b.ToArray(),
            DNVType.IntArray    => MemoryMarshal.Cast<byte, int>(b).ToArray(),
            DNVType.LongArray   => MemoryMarshal.Cast<byte, long>(b).ToArray(),
            DNVType.DoubleArray => MemoryMarshal.Cast<byte, double>(b).ToArray(),
            DNVType.StringArray => ReadStringArray(b),
            _ => throw new FormatException($"Unknown DNV value type: {(byte)code}"),
        };

        // ── Bajty -> żądany typ T ───────────────────────────────────────────────────────────

        /// <summary>
        /// Odczyt jako T. Liczby konwertują się między sobą (ułamki są ucinane: 5.7 -> 5), tekst na liczby i odwrotnie,
        /// tablice na listy, liczby i tekst na enumy. Gdy konwersja jest niemożliwa — InvalidCastException.
        /// </summary>
        internal static T Read<T>(DNVType code, ReadOnlySpan<byte> b, string valueName)
        {
            // Szybka ścieżka 1: zapisany typ prosty dokładnie taki, o jaki proszą (bez pakowania w obiekt)
            if (IsPrimitive(code) && typeof(T) == DNVProperties.ClrType(code))
                return Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(b));

            // Szybka ścieżka 2: liczba całkowita czytana jako inna liczba. Najczęstszy przypadek, bo Set(5) zapisuje
            // jeden bajt, a Get<int>() prosi o int.
            if (FitsInInt64(code) && TryFromInt64(ReadInt64(code, b), out T number))
                return number;

            // Ogólnie: dekodujemy to, co zapisano, i w razie potrzeby konwertujemy
            object decoded = Decode(code, b);
            if (decoded is T same)
                return same;

            try
            {
                return (T)ConvertTo(decoded, typeof(T));
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                throw new InvalidCastException(
                    $"DNV value \"{valueName}\" of type {decoded.GetType().Name} cannot be read as {typeof(T).Name}.", e);
            }
        }

        private static object ConvertTo(object v, Type target)
        {
            target = Nullable.GetUnderlyingType(target) ?? target;

            if (target.IsEnum)
                return v is string s ? Enum.Parse(target, s, ignoreCase: true) : Enum.ToObject(target, Convert.ToInt64(v, Inv));

            if (target == typeof(string))
                return v is Array ? throw new InvalidCastException() : v is IFormattable f ? f.ToString(null, Inv) : v.ToString()!;

            if (target == typeof(Guid) && v is string guidText)
                return Guid.Parse(guidText);

            if (v is Array)
                return v switch
                {
                    byte[]   a when target == typeof(List<byte>)   => new List<byte>(a),
                    int[]    a when target == typeof(List<int>)    => new List<int>(a),
                    long[]   a when target == typeof(List<long>)   => new List<long>(a),
                    double[] a when target == typeof(List<double>) => new List<double>(a),
                    string[] a when target == typeof(List<string>) => new List<string>(a),
                    _ => throw new InvalidCastException(),
                };

            // Ułamek -> liczba całkowita: ucinamy (jak rzutowanie (int)5.7), zamiast zaokrąglać jak Convert
            if (IsIntegerType(target))
                v = v switch
                {
                    float f   => MathF.Truncate(f),
                    double d  => Math.Truncate(d),
                    decimal m => decimal.Truncate(m),
                    _ => v,
                };

            return Convert.ChangeType(v, target, Inv);
        }

        // ── Pomocnicze ──────────────────────────────────────────────────────────────────────

        private static bool IsPrimitive(DNVType code) => code is DNVType.Bool or DNVType.Byte
            or DNVType.Short or DNVType.UShort or DNVType.Int or DNVType.UInt or DNVType.Long or DNVType.ULong
            or DNVType.Float or DNVType.Double;

        private static bool FitsInInt64(DNVType code) => code is DNVType.Byte
            or DNVType.Short or DNVType.UShort or DNVType.Int or DNVType.UInt or DNVType.Long;

        private static bool IsIntegerType(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(short)
            || t == typeof(byte) || t == typeof(sbyte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort);

        private static long ReadInt64(DNVType code, ReadOnlySpan<byte> b) => code switch
        {
            DNVType.Byte   => b[0],
            DNVType.Short  => MemoryMarshal.Read<short>(b),
            DNVType.UShort => MemoryMarshal.Read<ushort>(b),
            DNVType.Int    => MemoryMarshal.Read<int>(b),
            DNVType.UInt   => MemoryMarshal.Read<uint>(b),
            _              => MemoryMarshal.Read<long>(b),
        };

        /// <summary> long -> T dla typów liczbowych (zawężanie bez kontroli przepełnienia, jak zwykłe rzutowanie) </summary>
        private static bool TryFromInt64<T>(long v, out T result)
        {
            // Porównania typeof(T) JIT rozstrzyga przy kompilacji — zostaje jedna gałąź, bez pakowania w obiekt
            if      (typeof(T) == typeof(int))     result = (T)(object)(int)v;
            else if (typeof(T) == typeof(long))    result = (T)(object)v;
            else if (typeof(T) == typeof(short))   result = (T)(object)(short)v;
            else if (typeof(T) == typeof(byte))    result = (T)(object)(byte)v;
            else if (typeof(T) == typeof(sbyte))   result = (T)(object)(sbyte)v;
            else if (typeof(T) == typeof(uint))    result = (T)(object)(uint)v;
            else if (typeof(T) == typeof(ulong))   result = (T)(object)(ulong)v;
            else if (typeof(T) == typeof(ushort))  result = (T)(object)(ushort)v;
            else if (typeof(T) == typeof(double))  result = (T)(object)(double)v;
            else if (typeof(T) == typeof(float))   result = (T)(object)(float)v;
            else if (typeof(T) == typeof(decimal)) result = (T)(object)(decimal)v;
            else { result = default!; return false; }
            return true;
        }

        /// <summary> decimal zapisany jako decimal.GetBits: lo, mid, hi, flags (4 x int32) </summary>
        private static decimal ReadDecimal(ReadOnlySpan<byte> b)
        {
            Span<int> bits = stackalloc int[4];
            b[..16].CopyTo(MemoryMarshal.AsBytes(bits));
            return new decimal(bits);
        }

        /// <summary> Tablica tekstów: [int32 długość][UTF-8] powtórzone dla każdego elementu </summary>
        private static string[] ReadStringArray(ReadOnlySpan<byte> b)
        {
            var list = new List<string>();
            while (b.Length >= 4)
            {
                int length = BinaryPrimitives.ReadInt32LittleEndian(b);
                list.Add(Encoding.UTF8.GetString(b.Slice(4, length)));
                b = b[(4 + length)..];
            }
            return list.ToArray();
        }

        internal static byte[] WriteStringArray(string[] values)
        {
            int total = 0;
            foreach (string s in values)
                total += 4 + Encoding.UTF8.GetByteCount(s ?? string.Empty);

            byte[] bytes = new byte[total];
            int pos = 0;
            foreach (string s in values)
            {
                int length = Encoding.UTF8.GetBytes(s ?? string.Empty, bytes.AsSpan(pos + 4));
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(pos), length);
                pos += 4 + length;
            }
            return bytes;
        }

        internal static byte[] WriteDecimal(decimal value)
        {
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value, bits);
            return MemoryMarshal.AsBytes(bits).ToArray();
        }
    }
}
