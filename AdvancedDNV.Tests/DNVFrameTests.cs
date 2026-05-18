using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVFrameTests : DNVTestBase
    {
        public DNVFrameTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void DNVFrame_BasicRoundtrip_PreservesValues()
        {
            var guid = Guid.NewGuid();
            var frame = new DNVFrame();
            frame.main.Value("int").Set(42);
            frame.main.Value("str").Set("hello");
            frame.main.Value("guid").Set(guid);
            frame.main.Value("double").Set(3.14);

            byte[] bytes = frame.SendToByte();

            var frame2 = new DNVFrame(bytes);
            Assert.Equal(42,     frame2.main.Value("int").Get<int>());
            Assert.Equal("hello",frame2.main.Value("str").Get<string>());
            Assert.Equal(guid,   frame2.main.Value("guid").Get<Guid>());
            Assert.Equal(3.14,   frame2.main.Value("double").Get<double>(), 10);
        }

        [Fact]
        public void DNVFrame_NestedContainers_Roundtrip()
        {
            var frame = new DNVFrame();
            frame.main["section1"]["sub"].Value("x").Set(100);
            frame.main["section2"].Value("y").Set("nested string");

            byte[] bytes = frame.SendToByte();

            var frame2 = new DNVFrame(bytes);
            Assert.Equal(100,             frame2.main["section1"]["sub"].Value("x").Get<int>());
            Assert.Equal("nested string", frame2.main["section2"].Value("y").Get<string>());
        }

        [Fact]
        public void DNVFrame_SendToByte_ProducesDifferentBytesEachTime()
        {
            // Każde SendToByte() powinno zawierać różne dane (nowe sole / enkrypcja)
            var frame = new DNVFrame();
            frame.main.Value("v").Set(1);

            byte[] a = frame.SendToByte();
            byte[] b = frame.SendToByte();

            Assert.Equal(a.Length, b.Length);
        }

        [Fact]
        public void DNVFrame_LargeData_Roundtrip()
        {
            var rnd = new Random(42);
            int[] bigArray = new int[10_000];
            for (int i = 0; i < bigArray.Length; i++)
                bigArray[i] = rnd.Next();

            var frame = new DNVFrame();
            frame.main.Value("bigArray").Set(bigArray);

            byte[] bytes = frame.SendToByte();
            var frame2 = new DNVFrame(bytes);

            Assert.Equal(bigArray, frame2.main.Value("bigArray").Get<int[]>());
        }

        [Fact]
        public void BenchmarkDNVFrame_vs_JSON()
        {
            const int iterations       = 5;
            const int fieldsPerIteration = 2000;

            var sw  = new Stopwatch();
            var rng = new Random(42);

            var testData = new Dictionary<string, object>(fieldsPerIteration);
            for (int i = 0; i < fieldsPerIteration; i++)
            {
                testData[$"key_{i}"] = i % 3 == 0
                    ? (object)rng.Next(int.MinValue, int.MaxValue)
                    : i % 3 == 1
                        ? (object)(rng.NextDouble() * 10_000)
                        : (object)Guid.NewGuid();
            }

            // ── DNVFrame zapis ──────────────────────────────────────────────────
            byte[] dnvBytes = Array.Empty<byte>();
            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
            {
                var frame = new DNVFrame();
                foreach (var kv in testData)
                {
                    switch (kv.Value)
                    {
                        case int iv:    frame.main.Value(kv.Key).Set(iv); break;
                        case double dv: frame.main.Value(kv.Key).Set(dv); break;
                        case Guid gv:   frame.main.Value(kv.Key).Set(gv); break;
                    }
                }
                dnvBytes = frame.SendToByte();
            }
            long dnvWriteMs = sw.ElapsedMilliseconds;

            // ── JSON zapis ──────────────────────────────────────────────────────
            byte[] jsonBytes = Array.Empty<byte>();
            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
                jsonBytes = JsonSerializer.SerializeToUtf8Bytes(testData);
            long jsonWriteMs = sw.ElapsedMilliseconds;

            // ── DNVFrame odczyt ─────────────────────────────────────────────────
            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
            {
                var frame2 = new DNVFrame((byte[])dnvBytes.Clone());
                for (int i = 0; i < fieldsPerIteration; i++)
                {
                    string key = $"key_{i}";
                    if      (i % 3 == 0) _ = frame2.main.Value(key).Get<int>();
                    else if (i % 3 == 1) _ = frame2.main.Value(key).Get<double>();
                    else                 _ = frame2.main.Value(key).Get<Guid>();
                }
            }
            long dnvReadMs = sw.ElapsedMilliseconds;

            // ── JSON odczyt ─────────────────────────────────────────────────────
            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
                _ = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
            long jsonReadMs = sw.ElapsedMilliseconds;

            _output.WriteLine($"════════════════════════════════════════════");
            _output.WriteLine($"  Benchmark: DNVFrame vs JSON  ({iterations} iter, {fieldsPerIteration} pól)");
            _output.WriteLine($"════════════════════════════════════════════");
            _output.WriteLine($"  Rozmiar: DNVFrame={dnvBytes.Length} B  JSON={jsonBytes.Length} B");
            _output.WriteLine($"  Zapis:   DNVFrame={dnvWriteMs} ms  JSON={jsonWriteMs} ms");
            _output.WriteLine($"  Odczyt:  DNVFrame={dnvReadMs} ms  JSON={jsonReadMs} ms");
            _output.WriteLine($"════════════════════════════════════════════");
        }
    }
}
