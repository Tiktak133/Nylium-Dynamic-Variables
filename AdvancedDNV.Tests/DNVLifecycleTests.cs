using System;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVLifecycleTests : DNVTestBase
    {
        public DNVLifecycleTests(ITestOutputHelper output) : base(output) { }

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
            Assert.NotNull(dnv.main);
            dnv.Close();
        }

        [Fact]
        public void SaveAndOpen_WithNoPassword()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.Save();
            dnv.Close();

            var dnv2 = new DNV(testFilePath);
            bool opened = dnv2.Open();
            Assert.True(opened);
            Assert.True(dnv2.isOpened);
            dnv2.Close();
        }

        [Fact]
        public void SaveAndClose_SetsIsOpenedFalse()
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
        public void Open_CanBeCalledTwice_SecondCallIgnored()
        {
            var dnv = new DNV(testFilePath);
            bool first  = dnv.Open();
            bool second = dnv.Open();
            Assert.True(first);
            Assert.True(second);
            Assert.True(dnv.isOpened);
            dnv.Close();
        }

        [Fact]
        public void Save_WithoutOpen_DoesNotThrow()
        {
            var dnv = new DNV(testFilePath);
            var ex = Record.Exception(() => dnv.Save());
            Assert.Null(ex);
        }
    }
}
