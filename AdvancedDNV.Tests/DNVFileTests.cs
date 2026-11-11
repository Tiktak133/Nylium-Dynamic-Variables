using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AdvancedDNV.Tests
{
    /// <summary> Zapis plików: tylko przy zmianach, odroczony autozapis, klucz zmieniany co KeyLifetime, kopia .bak </summary>
    public class DNVFileTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"dnv_file_{Guid.NewGuid():N}.dnv");
        private string Bak => _path + ".bak";

        public void Dispose()
        {
            foreach (var p in new[] { _path, Bak, _path + "tmp" })
                try { File.Delete(p); } catch { }
        }

        private static T ReadOnce<T>(string path, string name, string password = "")
        {
            var dnv = new DNV(path, password, AutoSave: false);
            dnv.Open();
            T result = dnv.main.Value(name).Get<T>();
            dnv.Close();
            return result;
        }

        /// <summary> Zaszyfrowane bajty części main pliku: [16B sygnatura][2B wersja][4B długość metadanych][metadane][main] </summary>
        private static byte[] MainCipher(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            int metadataLength = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(18));
            return file[(22 + metadataLength)..];
        }

        // ── Zapis tylko przy zmianach ───────────────────────────────────────────────────────

        [Fact]
        public void Save_WithoutChanges_DoesNotRewriteFile()
        {
            var dnv = new DNV(_path, AutoSave: false);
            dnv.Open();
            dnv.main.Value("v").Set(1);
            dnv.Save();
            DateTime written = File.GetLastWriteTimeUtc(_path);

            Thread.Sleep(50);
            dnv.Save();
            dnv.SaveAndClose();

            Assert.Equal(written, File.GetLastWriteTimeUtc(_path));
        }

        // ── Autozapis ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void AutoSave_BurstOfChanges_SavesOnceAfterLastChange()
        {
            var dnv = new DNV(_path, AutoSaveDelayMs: 150);
            dnv.Open();
            for (int i = 0; i < 40; i++) // ~0,5 s zmian co ~10-16 ms — przerwy krótsze niż 150 ms
            {
                dnv.main.Value("n").Set(i);
                Thread.Sleep(10);
            }
            Thread.Sleep(1200);
            dnv.Close();

            var check = new DNV(_path, AutoSave: false);
            check.Open();
            Assert.Equal(39, check.main.Value("n").Get<int>());
            Assert.InRange(check.Meta.numberOfSaving, 1, 2); // wcześniej: zapis co 150 ms od pierwszej zmiany (~4 zapisy)
            check.Close();
        }

        [Fact]
        public void AutoSave_ContinuousChanges_AreSavedWithinMaxWait()
        {
            var dnv = new DNV(_path, AutoSaveDelayMs: 300); // najdłużej 5 x 300 ms = 1,5 s od pierwszej zmiany
            dnv.Open();

            bool savedWhileChanging = false;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2500) // zmiany co 50 ms — nigdy nie ma 300 ms ciszy
            {
                dnv.main.Value("n").Add(1);
                Thread.Sleep(50);
                savedWhileChanging |= File.Exists(_path);
            }
            dnv.Close();

            Assert.True(savedWhileChanging);
        }

        [Fact]
        public void AutoSave_FailedSave_IsRetriedInsteadOfCrashing()
        {
            var dnv = new DNV(_path, AutoSaveDelayMs: 20) { AutoSaveRetryMs = 200 };
            dnv.Open();
            dnv.main.Value("a").Set(1);
            dnv.Save();

            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None)) // np. antywirus trzyma plik
            {
                dnv.main.Value("a").Set(2);
                Thread.Sleep(400); // autozapis próbuje i nie może podmienić pliku
            }
            Thread.Sleep(1000); // ponowienie po 200 ms już się udaje

            Assert.Equal(2, ReadOnce<int>(_path, "a"));
            dnv.Close();
        }

        [Fact]
        public void AutoSave_ManyThreads_FinalStateIsSaved()
        {
            var dnv = new DNV(_path, AutoSaveDelayMs: 5);
            dnv.Open();

            using var stop = new CancellationTokenSource();
            var reader = Task.Run(() => // równolegle czytamy całe drzewo i wyodrębniamy ramki
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (var c in dnv.main.GetContainers())
                        _ = c.Value("n").Get<long>();
                    _ = dnv.main.ExtractToFrame().SendToByte();
                }
            });

            Parallel.For(0, 8, t =>
            {
                for (int i = 0; i <= 2000; i++)
                    dnv.main[$"t{t}"].Value("n").Set(i);
            });
            stop.Cancel();
            reader.Wait();

            Thread.Sleep(1500); // BEZ ręcznego Save — stan końcowy musi zapisać sam autozapis
            dnv.Close();

            var check = new DNV(_path, AutoSave: false);
            check.Open();
            for (int t = 0; t < 8; t++)
                Assert.Equal(2000, check.main[$"t{t}"].Value("n").Get<int>());
            check.Close();
        }

        // ── Klucz szyfrowania ───────────────────────────────────────────────────────────────

        [Fact]
        public void Password_Key_IsReusedWithinLifetime_AlsoAcrossSessions()
        {
            var dnv = new DNV(_path, "Haslo", AutoSave: false);
            dnv.Open();
            dnv.main.Value("x").Set("A"); dnv.Save();
            byte[] first = MainCipher(_path);
            dnv.main.Value("x").Set("B"); dnv.Save();
            dnv.main.Value("x").Set("A"); dnv.Save();
            Assert.Equal(first, MainCipher(_path)); // ten sam klucz i ta sama treść = te same bajty
            dnv.Close();

            var next = new DNV(_path, "Haslo", AutoSave: false); // klucz ma kilka sekund — kolejna sesja go używa
            next.Open();
            next.main.Value("x").Set("B"); next.Save();
            next.main.Value("x").Set("A"); next.Save();
            Assert.Equal(first, MainCipher(_path));
            next.Close();

            Assert.Equal("A", ReadOnce<string>(_path, "x", "Haslo"));
        }

        [Fact]
        public void Password_Key_IsRotatedAfterLifetime()
        {
            var dnv = new DNV(_path, "Haslo", AutoSave: false) { KeyLifetime = TimeSpan.Zero };
            dnv.Open();
            dnv.main.Value("x").Set("A"); dnv.Save();
            byte[] first = MainCipher(_path);
            dnv.main.Value("x").Set("B"); dnv.Save();
            dnv.main.Value("x").Set("A"); dnv.Save();
            Assert.NotEqual(first, MainCipher(_path)); // nowa sól = inne bajty
            dnv.Close();

            Assert.Equal("A", ReadOnce<string>(_path, "x", "Haslo"));
        }

        // ── Kopia .bak ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void Backup_KeepsStateFromBeforeTheSession()
        {
            var first = new DNV(_path, AutoSave: false);
            first.Open();
            first.main.Value("v").Set("v1");
            first.SaveAndClose();
            Assert.False(File.Exists(Bak)); // nowy plik — nie było czego kopiować

            var session = new DNV(_path, AutoSave: false);
            session.Open();
            session.main.Value("v").Set("v2"); session.Save(); // pierwszy zapis w sesji: v1 -> .bak
            session.main.Value("v").Set("v3"); session.Save(); // w ciągu 10 min .bak zostaje bez zmian
            session.Close();

            Assert.Equal("v1", ReadOnce<string>(Bak, "v"));
            Assert.Equal("v3", ReadOnce<string>(_path, "v"));
        }
    }
}
