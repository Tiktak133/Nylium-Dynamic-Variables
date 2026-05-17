using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string testFilePath;

        public DNVTests(ITestOutputHelper output)
        {
            _output = output;
            // create unique test file path in C:\DNV\
            Directory.CreateDirectory("C:\\DNV");
            testFilePath = Path.Combine("C:\\DNV", $"test_{Guid.NewGuid():N}.dnv");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(testFilePath))
                    File.Delete(testFilePath);
            }
            catch { }
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var dnv = new DNV(testFilePath);
            Assert.NotNull(dnv);
        }

        [Fact]
        public void Open_CreateNewFile_WhenFileDoesNotExist()
        {
            var dnv = new DNV(testFilePath);
            bool opened = dnv.Open();
            Assert.True(opened);
            Assert.True(dnv.isOpened);
            // main should be available
            Assert.NotNull(dnv.main);
            dnv.Close();
        }

        [Fact]
        public void SaveAndOpen_WithNoPassword()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            // add a value to main container if API available - here we just call Save
            dnv.Save();
            dnv.Close();

            var dnv2 = new DNV(testFilePath);
            bool opened = dnv2.Open();
            Assert.True(opened);
            Assert.True(dnv2.isOpened);
            dnv2.Close();
        }

        [Fact]
        public void SaveAndClose_Works()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.SaveAndClose();
            Assert.False(dnv.isOpened);
        }

        [Fact]
        public void Close_WithoutOpen_DoesNotThrow()
        {
            var dnv = new DNV(testFilePath);
            dnv.Close();
            Assert.False(dnv.isOpened);
        }

        [Fact]
        public void Main_Property_Throws_WhenNotOpened()
        {
            var dnv = new DNV(testFilePath);
            Assert.Throws<Exception>(() => { var m = dnv.main; });
        }

        [Fact]
        public void Metadata_Accessors_Work_AfterOpen()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            // Access some metadata
            var meta = dnv.Meta;
            Assert.False(meta.isCompressed);
            Assert.Equal(0, meta.numberOfOpening);
            dnv.Close();
        }

        [Fact]
        public void SaveAndOpen_ReadVariousTypes()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();

            var guid = Guid.NewGuid();
            var date = new DateTime(2020, 1, 2, 3, 4, 5);

            dnv.main.Value("ąćżźł").Set("polish");
            dnv.main.Value("intVal").Set(12345);
            dnv.main.Value("strVal").Set("hello world - polska górą  ąbćżźłr@");
            dnv.main.Value("boolVal").Set(true);
            dnv.main.Value("doubleVal").Set(3.1415926535);
            dnv.main.Value("dateVal").Set(date);
            dnv.main.Value("guidVal").Set(guid);

            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();

            Assert.Equal(12345, dnv2.main.Value("intVal").Get<int>());
            Assert.Equal("hello world - polska górą  ąbćżźłr@", dnv2.main.Value("strVal").Get<string>());
            Assert.Equal("polish", dnv2.main.Value("ąćżźł").Get<string>());
            Assert.True(dnv2.main.Value("boolVal").Get<bool>());
            Assert.Equal(3.1415926535, dnv2.main.Value("doubleVal").Get<double>(), 7);

            var readDate = dnv2.main.Value("dateVal").Get<DateTime>();
            Assert.Equal(date.Ticks, readDate.Ticks);

            var readGuid = dnv2.main.Value("guidVal").Get<Guid>();
            Assert.Equal(guid, readGuid);

            dnv2.Close();
        }

        [Fact]
        public void SaveAndOpen_ReadArraysAndBytes()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();

            byte[] byteArr = new byte[] { 1, 2, 3, 255 };
            int[] intArr = new int[] { 10, 20, 30 };
            string[] strArr = new string[] { "alpha", "beta", "łódź" };

            dnv.main.Value("byteArr").Set(byteArr);
            dnv.main.Value("intArr").Set(intArr);
            dnv.main.Value("strArr").Set(strArr);

            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();

            var readByteArr = dnv2.main.Value("byteArr").Get<byte[]>();
            Assert.Equal(byteArr, readByteArr);

            var readIntArr = dnv2.main.Value("intArr").Get<int[]>();
            Assert.Equal(intArr, readIntArr);

            var readStrArr = dnv2.main.Value("strArr").Get<string[]>();
            Assert.Equal(strArr, readStrArr);

            dnv2.Close();
        }

        [Fact]
        public void Open_OlderDnv_File_ReadsStructure()
        {
            // locate old.dnv in the test project folder
            string oldPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AdvancedDNV.Tests", "old.dnv"));
            Assert.True(File.Exists(oldPath), $"Test sample not found: {oldPath}");

            var dnv = new DNV(oldPath);
            bool opened = dnv.Open();
            Assert.True(opened);
            Assert.True(dnv.isOpened);

            // main container should be available and not throw
            var main = dnv.main;
            Assert.NotNull(main);

            // try to enumerate values/containers (should not throw)
            var containers = main.GetContainers();
            var values = main.GetValues();

            Assert.NotNull(containers);
            Assert.NotNull(values);

            dnv.Close();
        }

        [Fact]
        public void Open_Complex_File_ReadsStructure()
        {
            var swTotal = Stopwatch.StartNew();
            var sw = new Stopwatch();

            // ctor
            sw.Restart();
            var dnv = new DNV(testFilePath);
            sw.Stop();
            _output.WriteLine($"Ctor: {sw.Elapsed.TotalMilliseconds:F1} ms");

            // open
            sw.Restart();
            dnv.Open();
            sw.Stop();
            _output.WriteLine($"Open (initial): {sw.Elapsed.TotalMilliseconds:F1} ms");
            
            // prepare data
            sw.Restart();
            var rnd = new Random();
            int[] values = new int[1000];
            for (int i = 0; i < values.Length; i++)
                values[i] = rnd.Next(0, 1000);
            sw.Stop();
            _output.WriteLine($"Generate sample array (1000 ints): {sw.Elapsed.TotalMilliseconds:F1} ms");
            
            // write many values
            const int outer = 200;
            const int inner = 200;
            int totalSets = outer * inner;
            sw.Restart();
            for (int j = 0; j < outer; j++)
            {
                string sj = j.ToString();
                for (int i = 0; i < inner; i++)
                    dnv.main[sj][sj][sj].Value(i.ToString()).Set(values);
            }
            sw.Stop();
            
            _output.WriteLine($"Set {totalSets} values: {sw.Elapsed.TotalMilliseconds:F1} ms ({totalSets / Math.Max(1, sw.Elapsed.TotalSeconds):F0} ops/s)");
            
            // save & close
            sw.Restart();
            dnv.SaveAndClose();
            sw.Stop();
            _output.WriteLine($"SaveAndClose: {sw.Elapsed.TotalMilliseconds:F1} ms");
            
            // reopen (ctor + open)
            sw.Restart();
            var dnv2 = new DNV(testFilePath);
            sw.Stop();
            _output.WriteLine($"Ctor (reopen): {sw.Elapsed.TotalMilliseconds:F1} ms");

            sw.Restart();
            dnv2.Open();
            sw.Stop();
            _output.WriteLine($"Open (reopen): {sw.Elapsed.TotalMilliseconds:F1} ms");

            // verify random reads
            int sampleCount = 1000;
            var rndVerify = new Random();
            var seen = new HashSet<int>();

            sw.Restart();
            while (seen.Count < Math.Min(sampleCount, totalSets))
            {
                int idx = rndVerify.Next(0, totalSets); // indeks liniowy 0..totalSets-1
                if (!seen.Add(idx)) continue;

                int j = idx / inner;
                int i = idx % inner;

                int[] val = dnv2.main[j.ToString()][j.ToString()][j.ToString()].Value(i.ToString()).Get<int[]>();
                Assert.Equal(values, val);
            }
            sw.Stop();
            _output.WriteLine($"Get & Assert {seen.Count} unique random values: {sw.Elapsed.TotalMilliseconds:F1} ms");

            // metadata sizes
            _output.WriteLine($"initialDataSize: {dnv2.Meta.initialDataSize / 1024} kB");
            _output.WriteLine($"compressedDataSize: {dnv2.Meta.compressedDataSize / 1024} kB");


            dnv2.main.Value("testMeta").Set("testing metadata");
            
            // save & close
            sw.Restart();
            dnv2.SaveAndClose();
            sw.Stop();
            _output.WriteLine($"Second SaveAndClose: {sw.Elapsed.TotalMilliseconds:F1} ms");

            swTotal.Stop();
            _output.WriteLine($"Total test time: {swTotal.Elapsed.TotalMilliseconds:F1} ms ({swTotal.Elapsed.TotalSeconds:F3} s)");
            
        }

        [Fact]
        public void OpenOldCSharp48FileFormat_ConsiderableDataBlock()
        {
            TestOpeningFile(@"C:\Users\kamil\source\repos\Nylium Dynamic Variables\AdvancedDNV\discordVideo.dnv");
        }

        [Fact]
        public void OpenOldCSharp48FileFormat_SmallDataBlock()
        {
            TestOpeningFile(@"C:\Users\kamil\source\repos\Nylium Dynamic Variables\AdvancedDNV\update.dnv");
        }

        private void TestOpeningFile(string fileName)
        {
            var tempFile = @"temp_dataBlock.dnv";
            if (File.Exists(tempFile))
                File.Delete(tempFile);

            File.Copy(fileName, tempFile);

            var swTotal = Stopwatch.StartNew();
            var sw = new Stopwatch();

            // reopen (ctor + open)
            sw.Restart();
            var dnv2 = new DNV(fileName);
            sw.Stop();
            _output.WriteLine($"Ctor (reopen): {sw.Elapsed.TotalMilliseconds:F1} ms");

            sw.Restart();
            dnv2.Open();
            sw.Stop();
            _output.WriteLine($"Open (reopen): {sw.Elapsed.TotalMilliseconds:F1} ms");

            // verify random reads
            int sampleCount = 1000;
            var rndVerify = new Random();
            var seen = new HashSet<int>();

            /*
            sw.Restart();
            while (seen.Count < Math.Min(sampleCount, totalSets))
            {
                int idx = rndVerify.Next(0, totalSets); // indeks liniowy 0..totalSets-1
                if (!seen.Add(idx)) continue;

                int j = idx / inner;
                int i = idx % inner;

                int[] val = dnv2.main[j.ToString()][j.ToString()][j.ToString()].Value(i.ToString()).Get<int[]>();
                Assert.Equal(values, val);
            }
            sw.Stop();
            _output.WriteLine($"Get & Assert {seen.Count} unique random values: {sw.Elapsed.TotalMilliseconds:F1} ms");
            */

            // metadata sizes
            _output.WriteLine($"initialDataSize: {dnv2.Meta.initialDataSize / 1024} kB");
            _output.WriteLine($"compressedDataSize: {dnv2.Meta.compressedDataSize / 1024} kB");


            dnv2.main.Value("testMeta").Set("testing metadata");

            // save & close
            sw.Restart();
            dnv2.SaveAndClose();
            sw.Stop();
            _output.WriteLine($"Second SaveAndClose: {sw.Elapsed.TotalMilliseconds:F1} ms");

            swTotal.Stop();
            _output.WriteLine($"Total test time: {swTotal.Elapsed.TotalMilliseconds:F1} ms ({swTotal.Elapsed.TotalSeconds:F3} s)");

            File.Delete(tempFile);
        }

        [Fact]
        public void BenchmarkDNVFrame_vs_JSON()
        {
            const int iterations = 1;
            const int fieldsPerIteration = 2000;

            var sw = new Stopwatch();
            var rng = new Random(42);

            // ── Przygotowanie danych testowych ──────────────────────────────────
            var testData = new Dictionary<string, object>(fieldsPerIteration);
            for (int i = 0; i < fieldsPerIteration; i++)
            {
                testData[$"key_{i}"] = i % 3 == 0
                    ? (object)rng.Next(int.MinValue, int.MaxValue)
                    : i % 3 == 1
                        ? (object)(rng.NextDouble() * 10_000)
                        : (object)Guid.NewGuid();
            }

            // ════════════════════════════════════════════════════════════════════
            // DNVFrame – zapis (SendToByte)
            // ════════════════════════════════════════════════════════════════════
            long dnvWriteMs = 0;
            byte[] dnvBytes = Array.Empty<byte>();

            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
            {
                var frame = new DNVFrame();
                foreach (var kv in testData)
                {
                    switch (kv.Value)
                    {
                        case int iv:    frame.main.Value(kv.Key).Set(iv);  break;
                        case double dv: frame.main.Value(kv.Key).Set(dv);  break;
                        case Guid gv:   frame.main.Value(kv.Key).Set(gv);  break;
                    }
                }
                dnvBytes = frame.SendToByte();
            }
            sw.Stop();
            dnvWriteMs = sw.ElapsedMilliseconds;

            // ════════════════════════════════════════════════════════════════════
            // JSON – zapis (JsonSerializer.SerializeToUtf8Bytes)
            // ════════════════════════════════════════════════════════════════════
            long jsonWriteMs = 0;
            byte[] jsonBytes = Array.Empty<byte>();

            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
                jsonBytes = JsonSerializer.SerializeToUtf8Bytes(testData);
            sw.Stop();
            jsonWriteMs = sw.ElapsedMilliseconds;

            // ════════════════════════════════════════════════════════════════════
            // DNVFrame – odczyt (inicjalizacja z byte[])
            // ════════════════════════════════════════════════════════════════════
            long dnvReadMs = 0;

            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
            {
                var frameCopy = (byte[])dnvBytes.Clone();
                var frame2 = new DNVFrame(frameCopy);
                // odczyt wszystkich kluczy liczbowych i tekstowych
                for (int i = 0; i < fieldsPerIteration; i++)
                {
                    string key = $"key_{i}";
                    if (i % 3 == 0)      _ = frame2.main.Value(key).Get<int>();
                    else if (i % 3 == 1) _ = frame2.main.Value(key).Get<double>();
                    else                 _ = frame2.main.Value(key).Get<Guid>();
                }
            }
            sw.Stop();
            dnvReadMs = sw.ElapsedMilliseconds;

            // ════════════════════════════════════════════════════════════════════
            // JSON – odczyt (JsonSerializer.Deserialize)
            // ════════════════════════════════════════════════════════════════════
            long jsonReadMs = 0;

            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
                _ = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
            sw.Stop();
            jsonReadMs = sw.ElapsedMilliseconds;

            // ════════════════════════════════════════════════════════════════════
            // Operacje na danych – modyfikacja wartości w pętli
            // ════════════════════════════════════════════════════════════════════
            long dnvOpsMs = 0;

            var frameOps = new DNVFrame();
            for (int i = 0; i < fieldsPerIteration; i++)
                frameOps.main.Value($"key_{i}").Set(i);

            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
                for (int i = 0; i < fieldsPerIteration; i++)
                {
                    int current = frameOps.main.Value($"key_{i}").Get<int>();
                    frameOps.main.Value($"key_{i}").Set(current + 1);
                }
            sw.Stop();
            dnvOpsMs = sw.ElapsedMilliseconds;

            long jsonOpsMs = 0;
            var jsonDict = new Dictionary<string, int>(fieldsPerIteration);
            for (int i = 0; i < fieldsPerIteration; i++) jsonDict[$"key_{i}"] = i;

            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
                for (int i = 0; i < fieldsPerIteration; i++)
                    jsonDict[$"key_{i}"] = jsonDict[$"key_{i}"] + 1;
            sw.Stop();
            jsonOpsMs = sw.ElapsedMilliseconds;

            // ════════════════════════════════════════════════════════════════════
            // Wyniki
            // ════════════════════════════════════════════════════════════════════
            _output.WriteLine($"════════════════════════════════════════════");
            _output.WriteLine($"  Benchmark: DNVFrame vs JSON  ({iterations} iteracji, {fieldsPerIteration} pól)");
            _output.WriteLine($"════════════════════════════════════════════");
            _output.WriteLine($"  Rozmiar serializacji:");
            _output.WriteLine($"    DNVFrame : {dnvBytes.Length,8} B");
            _output.WriteLine($"    JSON     : {jsonBytes.Length,8} B");
            _output.WriteLine($"────────────────────────────────────────────");
            _output.WriteLine($"  Zapis (SerializeToByte / SerializeToUtf8Bytes):");
            _output.WriteLine($"    DNVFrame : {dnvWriteMs,6} ms   ({dnvWriteMs * 1.0 / iterations:F3} ms/iter)");
            _output.WriteLine($"    JSON     : {jsonWriteMs,6} ms   ({jsonWriteMs * 1.0 / iterations:F3} ms/iter)");
            _output.WriteLine($"    Stosunek : JSON jest {(dnvWriteMs > 0 ? (double)dnvWriteMs / jsonWriteMs : 0):F2}x wolniejszy/szybszy niż DNVFrame");
            _output.WriteLine($"────────────────────────────────────────────");
            _output.WriteLine($"  Odczyt (deserializacja + dostęp do pól):");
            _output.WriteLine($"    DNVFrame : {dnvReadMs,6} ms   ({dnvReadMs * 1.0 / iterations:F3} ms/iter)");
            _output.WriteLine($"    JSON     : {jsonReadMs,6} ms   ({jsonReadMs * 1.0 / iterations:F3} ms/iter)");
            _output.WriteLine($"    Stosunek : JSON jest {(dnvReadMs > 0 ? (double)dnvReadMs / jsonReadMs : 0):F2}x wolniejszy/szybszy niż DNVFrame");
            _output.WriteLine($"────────────────────────────────────────────");
            _output.WriteLine($"  Operacje na danych (odczyt + modyfikacja wartości):");
            _output.WriteLine($"    DNVFrame : {dnvOpsMs,6} ms   ({dnvOpsMs * 1.0 / iterations:F3} ms/iter)");
            _output.WriteLine($"    JSON     : {jsonOpsMs,6} ms   ({jsonOpsMs * 1.0 / iterations:F3} ms/iter)");
            _output.WriteLine($"    Stosunek : JSON jest {(dnvOpsMs > 0 ? (double)dnvOpsMs / jsonOpsMs : 0):F2}x wolniejszy/szybszy niż DNVFrame");
            _output.WriteLine($"════════════════════════════════════════════");
        }
    }
}
