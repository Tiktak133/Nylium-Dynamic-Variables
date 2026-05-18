using System;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVCompatibilityTests : DNVTestBase
    {
        public DNVCompatibilityTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Open_OlderDnv_File_ReadsStructure()
        {
            string oldPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "AdvancedDNV.Tests", "old.dnv"));
            Assert.True(File.Exists(oldPath), $"Test sample not found: {oldPath}");

            var dnv = new DNV(oldPath);
            bool opened = dnv.Open();
            Assert.True(opened);
            Assert.True(dnv.isOpened);
            Assert.NotNull(dnv.main);
            Assert.NotNull(dnv.main.GetContainers());
            Assert.NotNull(dnv.main.GetValues());
            dnv.Close();
        }

        [Fact]
        public void Open_Complex_File_ReadsStructure()
        {
            var swTotal = Stopwatch.StartNew();
            var sw      = new Stopwatch();

            sw.Restart();
            var dnv = new DNV(testFilePath);
            dnv.Open();
            sw.Stop();
            _output.WriteLine($"Open: {sw.Elapsed.TotalMilliseconds:F1} ms");

            var rnd    = new Random();
            int[] data = new int[1000];
            for (int i = 0; i < data.Length; i++) data[i] = rnd.Next(0, 1000);

            const int outer = 200, inner = 200;
            sw.Restart();
            for (int j = 0; j < outer; j++)
            {
                string sj = j.ToString();
                for (int i = 0; i < inner; i++)
                    dnv.main[sj][sj][sj].Value(i.ToString()).Set(data);
            }
            sw.Stop();
            _output.WriteLine($"Set {outer * inner} values: {sw.Elapsed.TotalMilliseconds:F1} ms");

            sw.Restart();
            dnv.SaveAndClose();
            sw.Stop();
            _output.WriteLine($"SaveAndClose: {sw.Elapsed.TotalMilliseconds:F1} ms");

            sw.Restart();
            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            sw.Stop();
            _output.WriteLine($"Reopen: {sw.Elapsed.TotalMilliseconds:F1} ms");

            // Weryfikacja losowych odczytów
            var seen = new System.Collections.Generic.HashSet<int>();
            var rndV = new Random();
            while (seen.Count < 1000)
            {
                int idx = rndV.Next(0, outer * inner);
                if (!seen.Add(idx)) continue;
                int j = idx / inner, i = idx % inner;
                int[] val = dnv2.main[j.ToString()][j.ToString()][j.ToString()].Value(i.ToString()).Get<int[]>();
                Assert.Equal(data, val);
            }

            _output.WriteLine($"initialDataSize: {dnv2.Meta.initialDataSize / 1024} kB");
            dnv2.SaveAndClose();

            swTotal.Stop();
            _output.WriteLine($"Total: {swTotal.Elapsed.TotalSeconds:F3} s");
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
            Assert.True(File.Exists(fileName), $"Test file not found: {fileName}");

            var tempFile = @"temp_dataBlock.dnv";
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            File.Copy(fileName, tempFile);

            var swTotal = Stopwatch.StartNew();
            var sw = new Stopwatch();

            sw.Restart();
            var dnv2 = new DNV(fileName);
            sw.Stop();
            _output.WriteLine($"Ctor (reopen): {sw.Elapsed.TotalMilliseconds:F1} ms");

            sw.Restart();
            dnv2.Open();
            sw.Stop();
            _output.WriteLine($"Open (reopen): {sw.Elapsed.TotalMilliseconds:F1} ms");

            _output.WriteLine($"initialDataSize: {dnv2.Meta.initialDataSize / 1024} kB");
            _output.WriteLine($"compressedDataSize: {dnv2.Meta.compressedDataSize / 1024} kB");

            dnv2.main.Value("testMeta").Set("testing metadata");

            sw.Restart();
            dnv2.SaveAndClose();
            sw.Stop();
            _output.WriteLine($"Second SaveAndClose: {sw.Elapsed.TotalMilliseconds:F1} ms");

            swTotal.Stop();
            _output.WriteLine($"Total test time: {swTotal.Elapsed.TotalMilliseconds:F1} ms ({swTotal.Elapsed.TotalSeconds:F3} s)");

            File.Delete(tempFile);
        }
    }
}
