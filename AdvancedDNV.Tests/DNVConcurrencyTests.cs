using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVConcurrencyTests : DNVTestBase
    {
        public DNVConcurrencyTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void AutoSave_ConcurrentOperations_DoesNotThrow()
        {
            var dnv = new DNV(testFilePath, AutoSaveDelayMs: 1);
            dnv.Open();

            const int threads    = 8;
            const int durationMs = 3000;

            var errors  = new ConcurrentBag<Exception>();
            var cts     = new CancellationTokenSource(durationMs);
            var barrier = new Barrier(threads);

            var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
            {
                barrier.SignalAndWait();

                var local = new Random(threadId * 1337);

                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        string key   = $"t{threadId}_k{local.Next(0, 20)}";
                        string cname = $"c{threadId}_{local.Next(0, 5)}";
                        string sub   = $"sub{local.Next(0, 3)}";

                        switch (local.Next(0, 10))
                        {
                            case 0: dnv.main.Value(key).Set(local.Next());                                         break;
                            case 1: dnv.main.Value(key).Set(local.NextDouble() * 1e6);                             break;
                            case 2: dnv.main.Value(key).Set(Guid.NewGuid());                                       break;
                            case 3: dnv.main.Value(key).Set($"str_{local.Next()}");                                break;
                            case 4: dnv.main.Value(key).Set(DateTime.UtcNow);                                      break;
                            case 5: dnv.main.Value(key).Set(new int[]  { local.Next(), local.Next() });            break;
                            case 6: dnv.main.Value(key).Set(new byte[] { (byte)local.Next(0, 256) });              break;
                            case 7:
                                if (dnv.main.Value(key).IsSet()) _ = dnv.main.Value(key).Get();
                                break;
                            case 8:
                                dnv.main[cname][sub].Value(key).Set(local.Next());
                                _ = dnv.main[cname][sub].Value(key).Get<int>();
                                break;
                            case 9:
                                if (dnv.main.Value(key).IsSet()) dnv.main.Value(key).Drop();
                                break;
                        }
                    }
                    catch (Exception ex) { errors.Add(ex); }
                }
            })).ToArray();

            Task.WaitAll(tasks);
            dnv.SaveAndClose();

            _output.WriteLine($"Wątki: {threads}, czas: {durationMs} ms, AutoSave: 1 ms");
            _output.WriteLine($"Błędy: {errors.Count}");
            foreach (var e in errors.ToList().Take(5))
                _output.WriteLine($"  [ERR] {e.GetType().Name}: {e.Message}");

            Assert.Empty(errors);

            var dnv2 = new DNV(testFilePath, AutoSaveDelayMs: -1);
            Assert.True(dnv2.Open());
            Assert.NotNull(dnv2.main);
            dnv2.Close();
        }

        [Fact]
        public void ManualSave_CalledFromMultipleThreads_DoesNotThrow()
        {
            var dnv = new DNV(testFilePath, AutoSaveDelayMs: -1);
            dnv.Open();
            dnv.main.Value("init").Set(0);

            const int threadCount = 4;
            var errors  = new ConcurrentBag<Exception>();
            var barrier = new Barrier(threadCount);

            var tasks = Enumerable.Range(0, threadCount).Select(id => Task.Run(() =>
            {
                barrier.SignalAndWait();
                var local = new Random(id);
                for (int i = 0; i < 200; i++)
                {
                    try
                    {
                        dnv.main.Value($"k{id}_{i % 10}").Set(local.Next());
                        if (i % 20 == 0) dnv.Save();
                    }
                    catch (Exception ex) { errors.Add(ex); }
                }
            })).ToArray();

            Task.WaitAll(tasks);
            dnv.SaveAndClose();

            Assert.Empty(errors);
        }
    }
}
