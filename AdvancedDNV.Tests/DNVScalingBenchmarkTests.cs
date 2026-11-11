using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    /// <summary>
    /// Benchmark skalujący DNVFrame vs JSON.
    /// Mierzy zapis i odczyt dla rosnącej liczby pól i wskazuje punkt,
    /// w którym JSON zaczyna wygrywać z DNVFrame.
    /// </summary>
    public class DNVScalingBenchmarkTests : DNVTestBase
    {
        // Rozmiary do przetestowania — od małych do dużych zbiorów
        private static readonly int[] FieldCounts =
        [
            10, 25, 50, 100, 200, 500,
            1_000, 2_000, 5_000, 10_000, 20_000
        ];

        private const int WarmupIterations = 2;  // iteracje JIT warm-up (nie liczone)
        private const int MeasureIterations = 5; // iteracje pomiarowe

        public DNVScalingBenchmarkTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void BenchmarkScaling_DNVFrame_vs_JSON_WriteAndRead()
        {
            var sw = new Stopwatch();

            // ── Nagłówek tabeli ──────────────────────────────────────────────────
            _output.WriteLine("");
            _output.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
            _output.WriteLine("  Benchmark skalujący: DNVFrame vs JSON  (średnia z " + MeasureIterations + " iteracji)");
            _output.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
            _output.WriteLine($"  {"Pola",6}  {"DNV zapis",11}  {"JSON zapis",11}  {"Zapis winner",14}  {"DNV odczyt",11}  {"JSON odczyt",11}  {"Odczyt winner",14}");
            _output.WriteLine("  ──────────────────────────────────────────────────────────────────────────────");

            int? writeBreakEven = null;
            int? readBreakEven  = null;

            foreach (int fields in FieldCounts)
            {
                // Przygotuj dane testowe (deterministyczne — ten sam seed)
                var rng      = new Random(42);
                var testData = new Dictionary<string, object>(fields);
                for (int i = 0; i < fields; i++)
                {
                    testData[$"key_{i}"] = i % 3 == 0
                        ? (object)rng.Next(int.MinValue, int.MaxValue)
                        : i % 3 == 1
                            ? (object)(rng.NextDouble() * 10_000)
                            : (object)Guid.NewGuid();
                }

                // ── Warm-up (eliminacja JIT) ─────────────────────────────────────
                for (int w = 0; w < WarmupIterations; w++)
                {
                    RunDNVWrite(testData, fields);
                    RunJSONWrite(testData);
                    byte[] dnvWarm = RunDNVWrite(testData, fields);
                    byte[] jsonWarm = RunJSONWrite(testData);
                    RunDNVRead(dnvWarm, fields);
                    RunJSONRead(jsonWarm);
                }

                // ── Pomiar zapisu ────────────────────────────────────────────────
                byte[] dnvBytes  = Array.Empty<byte>();
                byte[] jsonBytes = Array.Empty<byte>();

                sw.Restart();
                for (int it = 0; it < MeasureIterations; it++)
                    dnvBytes = RunDNVWrite(testData, fields);
                double dnvWriteUs = sw.Elapsed.TotalMicroseconds / MeasureIterations;

                sw.Restart();
                for (int it = 0; it < MeasureIterations; it++)
                    jsonBytes = RunJSONWrite(testData);
                double jsonWriteUs = sw.Elapsed.TotalMicroseconds / MeasureIterations;

                // ── Pomiar odczytu ───────────────────────────────────────────────
                sw.Restart();
                for (int it = 0; it < MeasureIterations; it++)
                    RunDNVRead((byte[])dnvBytes.Clone(), fields);
                double dnvReadUs = sw.Elapsed.TotalMicroseconds / MeasureIterations;

                sw.Restart();
                for (int it = 0; it < MeasureIterations; it++)
                    RunJSONRead(jsonBytes);
                double jsonReadUs = sw.Elapsed.TotalMicroseconds / MeasureIterations;

                // ── Wyniki ───────────────────────────────────────────────────────
                string writeWinner = dnvWriteUs <= jsonWriteUs
                    ? $"DNV  {jsonWriteUs / dnvWriteUs:F2}x"
                    : $"JSON {dnvWriteUs / jsonWriteUs:F2}x";

                string readWinner = dnvReadUs <= jsonReadUs
                    ? $"DNV  {jsonReadUs / dnvReadUs:F2}x"
                    : $"JSON {dnvReadUs / jsonReadUs:F2}x";

                _output.WriteLine(
                    $"  {fields,6}  " +
                    $"{FormatUs(dnvWriteUs),11}  {FormatUs(jsonWriteUs),11}  {writeWinner,14}  " +
                    $"{FormatUs(dnvReadUs),11}  {FormatUs(jsonReadUs),11}  {readWinner,14}");

                // ── Wykrywanie punktu przecięcia ─────────────────────────────────
                if (writeBreakEven == null && dnvWriteUs > jsonWriteUs)
                    writeBreakEven = fields;

                if (readBreakEven == null && dnvReadUs > jsonReadUs)
                    readBreakEven = fields;
            }

            // ── Podsumowanie ─────────────────────────────────────────────────────
            _output.WriteLine("  ──────────────────────────────────────────────────────────────────────────────");
            _output.WriteLine("");
            _output.WriteLine("  PODSUMOWANIE");
            _output.WriteLine($"  Zapis  — JSON zaczyna wygrywać od: {(writeBreakEven.HasValue ? $"~{writeBreakEven} pól" : "nie zaobserwowano (DNV zawsze szybszy)")}");
            _output.WriteLine($"  Odczyt — JSON zaczyna wygrywać od: {(readBreakEven.HasValue  ? $"~{readBreakEven} pól"  : "nie zaobserwowano (DNV zawsze szybszy)")}");
            _output.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
            _output.WriteLine("");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static byte[] RunDNVWrite(Dictionary<string, object> data, int fields)
        {
            var frame = new DNVFrame();
            for (int i = 0; i < fields; i++)
            {
                string key = $"key_{i}";
                switch (data[key])
                {
                    case int    iv: frame.main.Value(key).Set(iv); break;
                    case double dv: frame.main.Value(key).Set(dv); break;
                    case Guid   gv: frame.main.Value(key).Set(gv); break;
                }
            }
            return frame.SendToByte();
        }

        private static byte[] RunJSONWrite(Dictionary<string, object> data)
            => JsonSerializer.SerializeToUtf8Bytes(data);

        private static void RunDNVRead(byte[] bytes, int fields)
        {
            var frame = new DNVFrame(bytes);
            for (int i = 0; i < fields; i++)
            {
                string key = $"key_{i}";
                if      (i % 3 == 0) _ = frame.main.Value(key).Get<int>();
                else if (i % 3 == 1) _ = frame.main.Value(key).Get<double>();
                else                 _ = frame.main.Value(key).Get<Guid>();
            }
        }

        private static void RunJSONRead(byte[] bytes)
            => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes);

        private static string FormatUs(double us)
        {
            if (us >= 1_000_000) return $"{us / 1_000_000:F2} s  ";
            if (us >= 1_000)     return $"{us / 1_000:F2} ms ";
            return $"{us:F1} µs ";
        }
    }
}
