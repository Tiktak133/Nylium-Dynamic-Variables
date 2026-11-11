using System;
using System.IO;
using Xunit;

namespace AdvancedDNV.Tests
{
    /// <summary>
    /// Zgodność formatu ze starszymi wersjami biblioteki. Pliki w Fixtures zapisały:
    ///   "feb" — wersja z lutego 2026 (commit 2eaa5df),
    ///   "now" — wersja wdrożona przed uproszczeniem (2026.0814.1902.0).
    /// Te same dane w obu przypadkach (patrz <see cref="Fill"/>), pliki "pass" z hasłem <see cref="Password"/>.
    /// </summary>
    public class DNVFormatCompatibilityTests
    {
        private const string Password = "Haslo123!";

        private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        /// <summary> Dokładnie te same wywołania, którymi zapisano pliki w Fixtures (kolejność ma znaczenie dla bajtów) </summary>
        private static void Fill(Container c)
        {
            c["user"].Value("name").Set("Testy zażółć");
            c["user"].Value("small").Set(5);
            c["user"].Value("big").Set(70000);
            c["user"].Value("neg").Set(-40000);
            c["user"].Value("huge").Set(5_000_000_000L);
            c["user"]["deep"]["deeper"].Value("pi").Set(3.14159265358979);
            c["user"]["deep"].Value("flag").Set(true);
            c["user"]["deep"].Value("when").Set(new DateTime(2026, 11, 11, 12, 30, 0));
            c["user"]["deep"].Value("guid").Set(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
            c["user"].Value("bytes").Set(new byte[] { 1, 2, 3, 4, 5 });
            c["user"].Value("list").Set(new string[] { "a", "bę", "c" });
        }

        private static void AssertFilled(Container c)
        {
            var u = c["user"];
            Assert.Equal("Testy zażółć",           u.Value("name").Get<string>());
            Assert.Equal(5L,                        u.Value("small").Get<long>());
            Assert.Equal(70000L,                    u.Value("big").Get<long>());
            Assert.Equal(-40000L,                   u.Value("neg").Get<long>());
            Assert.Equal(5_000_000_000L,            u.Value("huge").Get<long>());
            Assert.Equal(3.14159265358979,          u["deep"]["deeper"].Value("pi").Get<double>());
            Assert.True(                            u["deep"].Value("flag").Get<bool>());
            Assert.Equal(new DateTime(2026, 11, 11, 12, 30, 0), u["deep"].Value("when").Get<DateTime>());
            Assert.Equal(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), u["deep"].Value("guid").Get<Guid>());
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 },  u.Value("bytes").Get<byte[]>());
            Assert.Equal(new[] { "a", "bę", "c" },      u.Value("list").Get<string[]>());
        }

        [Theory]
        [InlineData("feb")]
        [InlineData("now")]
        public void Frame_WrittenByOlderVersion_IsReadable(string version)
        {
            var frame = new DNVFrame(File.ReadAllBytes(Fixture($"frame_{version}.bin")));
            AssertFilled(frame.main);
        }

        [Theory]
        [InlineData("feb", "plain", "")]
        [InlineData("feb", "pass", Password)]
        [InlineData("now", "plain", "")]
        [InlineData("now", "pass", Password)]
        public void File_WrittenByOlderVersion_IsReadable(string version, string kind, string password)
        {
            var dnv = new DNV(Fixture($"file_{version}_{kind}.dnv"), password, AutoSave: false);
            Assert.True(dnv.Open());
            AssertFilled(dnv.main);
            Assert.True(dnv.Meta.numberOfSaving >= 1);
            dnv.Close(); // bez zapisu — wzorce zostają nietknięte
        }

        [Fact]
        public void Frame_Bytes_AreIdenticalToDeployedVersion()
        {
            var frame = new DNVFrame();
            Fill(frame.main);
            Assert.Equal(File.ReadAllBytes(Fixture("frame_now.bin")), frame.SendToByte());
        }

        /// <summary>
        /// Przypadki brzegowe, które mogłyby zmienić to, co widzi serwer na starszej bibliotece: puste wartości
        /// (nie są wysyłane), nieustawione wartości, usunięte kontenery, liczby przez object, decimal, listy.
        /// </summary>
        [Fact]
        public void Frame_EdgeCases_BytesIdenticalToDeployedVersion()
        {
            var f = new DNVFrame();
            var m = f.main;
            m.Value("empty").Set("");
            m.Value("emptyArr").Set(new int[0]);
            _ = m.Value("touched");
            _ = m["onlyUnset"].Value("x");
            m.Value("dropped").Set(1); m.Value("dropped").Drop(); m.Value("readded").Set(2);
            m["c"].Value("s").Set((object)(short)-5);
            m["c"].Value("us").Set((object)(ushort)60000);
            m["c"].Value("ui").Set((object)4000000000u);
            m["c"].Value("dec").Set(0.1234567890123456789m);
            m["c"].Value("decShort").Set(2.5m);
            m["c"].Value("dbl").Set(1.0 / 3);
            m["c"].Value("neg").Set(-1);
            m["c"].Value("min").Set(long.MinValue);
            m["c"].Value("ts").Set(TimeSpan.FromMinutes(90));
            m["c"]["d"].Value("v").Set(1); m["c"]["d"].DropContainer();
            m["e"]["f"].Value("g").Set(true); m["e"]["f"].DropContainer();
            m.Value("bytes").Set(new byte[] { 9, 8, 7 });
            m.Value("list").Set(new System.Collections.Generic.List<string> { "x", "", "ąę" });
            m.Value("ints").Set(new System.Collections.Generic.List<int> { 1, -2, 300000 });
            m.Value("longs").Set(new long[] { 1, long.MaxValue });
            m.Value("doubles").Set(new double[] { 0.5, double.MinValue });
            m.Value("nullObj").Set((object?)null);
            m.Value("overwritten").Set("tekst"); m.Value("overwritten").Set(7);

            Assert.Equal(File.ReadAllBytes(Fixture("frame_edge_now.bin")), f.SendToByte());
        }

        [Fact]
        public void Frame_RoundTrip_PreservesAllTypes()
        {
            var frame = new DNVFrame();
            Fill(frame.main);
            AssertFilled(new DNVFrame(frame.SendToByte()).main);
        }

        [Fact]
        public void File_RoundTrip_WithPassword_PreservesAllTypes()
        {
            string path = Path.Combine(Path.GetTempPath(), $"dnv_compat_{Guid.NewGuid():N}.dnv");
            try
            {
                var dnv = new DNV(path, Password, AutoSave: false);
                dnv.Open();
                Fill(dnv.main);
                dnv.SaveAndClose();

                var reopened = new DNV(path, Password, AutoSave: false);
                reopened.Open();
                AssertFilled(reopened.main);
                Assert.True(reopened.Meta.isEncrypted);
                reopened.Close();
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
