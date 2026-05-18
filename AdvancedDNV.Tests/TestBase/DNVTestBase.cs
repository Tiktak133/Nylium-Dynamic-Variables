using System;
using System.IO;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public abstract class DNVTestBase : IDisposable
    {
        protected readonly ITestOutputHelper _output;
        protected readonly string testFilePath;

        protected DNVTestBase(ITestOutputHelper output)
        {
            _output = output;
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
    }
}
