using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace AdvancedDNV.Tests
{
    /// <summary>
    /// Odczyt wartości w innym typie, niż zapisano. Część przypadków psuła "optymalizacja" z maja 2026
    /// (InvalidCastException, śmieci przy decimal, ulong zapisywany jako float) — wersja z lutego działała.
    /// Każdy test przechodzi przez pełny zapis i odczyt ramki, więc sprawdza też format.
    /// </summary>
    public class DNVConversionTests
    {
        private static Value RoundTrip(Action<Value> set)
        {
            var frame = new DNVFrame();
            set(frame.main.Value("x"));
            return new DNVFrame(frame.SendToByte()).main.Value("x");
        }

        // ── Regresje z maja 2026 ────────────────────────────────────────────────────────────

        [Fact] public void Int_ReadAsDouble()  => Assert.Equal(5.0,  RoundTrip(v => v.Set(5)).Get<double>());
        [Fact] public void Int_ReadAsFloat()   => Assert.Equal(5f,   RoundTrip(v => v.Set(5)).Get<float>());
        [Fact] public void Int_ReadAsDecimal() => Assert.Equal(5m,   RoundTrip(v => v.Set(5)).Get<decimal>());
        [Fact] public void WholeDouble_ReadAsInt()  => Assert.Equal(5,  RoundTrip(v => v.Set(5.0)).Get<int>());
        [Fact] public void WholeDouble_ReadAsLong() => Assert.Equal(5L, RoundTrip(v => v.Set(5.0)).Get<long>());

        [Fact]
        public void Fraction_ReadAsInt_IsTruncated()
        {
            Assert.Equal(5,  RoundTrip(v => v.Set(5.7)).Get<int>());
            Assert.Equal(-5, RoundTrip(v => v.Set(-5.7)).Get<int>());
        }

        [Fact]
        public void PreciseDecimal_RoundTrip()
        {
            decimal d = 0.1234567890123456789m;
            Assert.Equal(d, RoundTrip(v => v.Set(d)).Get<decimal>());
        }

        [Fact]
        public void BigULong_RoundTrip()
        {
            ulong big = 18446744073709551000UL;
            var value = RoundTrip(v => v.Set(big));
            Assert.Equal(typeof(ulong), value.Type);
            Assert.Equal(big, value.Get<ulong>());
        }

        [Fact] public void Int_ReadAsString()      => Assert.Equal("5",   RoundTrip(v => v.Set(5)).Get<string>());
        [Fact] public void Double_ReadAsString()   => Assert.Equal("3.5", RoundTrip(v => v.Set(3.5)).Get<string>());
        [Fact] public void NumericString_ReadAsInt() => Assert.Equal(123, RoundTrip(v => v.Set("123")).Get<int>());

        // ── Pozostałe konwersje ─────────────────────────────────────────────────────────────

        [Fact]
        public void SmallInt_DynamicGet_ReturnsStoredType()
        {
            var value = RoundTrip(v => v.Set(5));
            Assert.IsType<byte>((object)value.Get()!);
            Assert.Equal(typeof(byte), value.Type);
        }

        [Fact]
        public void Enum_StoredAsName_ReadAsEnumOrString()
        {
            var value = RoundTrip(v => v.Set((object)DayOfWeek.Friday));
            Assert.Equal("Friday",         value.Get<string>());
            Assert.Equal(DayOfWeek.Friday, value.Get<DayOfWeek>());
            Assert.Equal(DayOfWeek.Friday, RoundTrip(v => v.Set(5)).Get<DayOfWeek>());
        }

        [Fact]
        public void EmptyString_IsNotSent_LikeOlderVersions()
        {
            // Serwer na starszej bibliotece rozpoznaje brak pola po IsSet() — puste wartości nie mogą trafić na łącze
            Assert.False(RoundTrip(v => v.Set("")).IsSet());
            Assert.False(RoundTrip(v => v.Set(new int[0])).IsSet());

            var frame = new DNVFrame();
            Assert.Equal("", frame.main.Value("x").Set("").Get<string>()); // w pamięci "" zostaje ""
        }

        [Fact]
        public void ByteArrayWithLength_StoresOnlyThatPart()
        {
            byte[] pooled = { 1, 2, 3, 99, 99, 99, 99, 99 };
            var frame = new DNVFrame();
            frame.main.Value("x").Set(pooled, 3);
            Assert.Equal(new byte[] { 1, 2, 3 }, frame.main.Value("x").Get<byte[]>());
            Assert.Equal(new byte[] { 1, 2, 3 }, new DNVFrame(frame.SendToByte()).main.Value("x").Get<byte[]>());
        }

        [Fact]
        public void Arrays_ReadAsLists()
        {
            Assert.Equal(new List<long> { 1, -2, long.MaxValue }, RoundTrip(v => v.Set(new long[] { 1, -2, long.MaxValue })).Get<List<long>>());
            Assert.Equal(new List<double> { 1.5, -2.25 },        RoundTrip(v => v.Set(new double[] { 1.5, -2.25 })).Get<List<double>>());
        }

        [Fact]
        public void ImpossibleConversion_ThrowsInvalidCast()
        {
            Assert.Throws<InvalidCastException>(() => RoundTrip(v => v.Set(new[] { 1, 2 })).Get<int>());
            Assert.Throws<InvalidCastException>(() => RoundTrip(v => v.Set("abc")).Get<int>());
        }

        [Fact]
        public void Add_ToFraction_KeepsFraction()
        {
            var frame = new DNVFrame();
            frame.main.Value("x").Set(1.5).Add(1);
            Assert.Equal(2.5, frame.main.Value("x").Get<double>());
        }

        [Fact]
        public void SmallValues_SharedArrays_StayIntact()
        {
            // bool i liczby 0-255 korzystają ze wspólnych tablic — żadna operacja nie może ich zmienić
            for (int round = 0; round < 3; round++)
            {
                var frame = new DNVFrame();
                for (int i = 0; i < 256; i++)
                {
                    frame.main["n"].Value("v" + i).Set(i);
                    frame.main["b"].Value("v" + i).Set(i % 2 == 0);
                }
                var copy = new DNVFrame(frame.SendToByte());
                _ = frame.main.ExtractToFrame().SendToByte();
                for (int i = 0; i < 256; i++)
                {
                    Assert.Equal(i,          copy.main["n"].Value("v" + i).Get<int>());
                    Assert.Equal(i % 2 == 0, copy.main["b"].Value("v" + i).Get<bool>());
                }
            }
            Assert.Equal(7, new DNVFrame().main.Value("x").Set(7).Get<int>());
            Assert.True(new DNVFrame().main.Value("x").Set(true).Get<bool>());
        }

        [Fact]
        public void SetNull_ClearsValue()
        {
            var frame = new DNVFrame();
            var value = frame.main.Value("x").Set("tekst");
            value.Set((string?)null);
            Assert.False(value.IsSet());
            Assert.Null(value.Get<string>());
        }

        // ── Ramki i pliki ───────────────────────────────────────────────────────────────────

        [Fact]
        public void Frame_WithEmptyMain_CanBeParsed()
        {
            var frame = new DNVFrame(new DNVFrame().SendToByte());
            Assert.Empty(frame.main.GetValues());
            Assert.Empty(frame.main.GetContainers());
        }

        [Fact]
        public void Frame_Damaged_ThrowsFormatException()
        {
            var frame = new DNVFrame();
            frame.main["a"].Value("b").Set("c");
            byte[] bytes = frame.SendToByte();
            Assert.Throws<FormatException>(() => new DNVFrame(bytes[..10]));
        }

        [Fact]
        public void Container_EmptyName_Throws()
        {
            var frame = new DNVFrame();
            Assert.Throws<Exception>(() => _ = frame.main[""]);
        }

        [Fact]
        public void File_WrongPassword_ThrowsCryptographicException()
        {
            string path = Path.Combine(Path.GetTempPath(), $"dnv_pass_{Guid.NewGuid():N}.dnv");
            try
            {
                var dnv = new DNV(path, "Poprawne1", AutoSave: false);
                dnv.Open();
                dnv.main.Value("tajne").Set("dane");
                dnv.SaveAndClose();

                // Wcześniej kontrola hasła była nieskuteczna i ok. 1 na 150 prób otwierała pusty plik zamiast błędu
                for (int i = 0; i < 300; i++)
                    Assert.ThrowsAny<CryptographicException>(() => new DNV(path, "Zle" + i, AutoSave: false).Open());
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
