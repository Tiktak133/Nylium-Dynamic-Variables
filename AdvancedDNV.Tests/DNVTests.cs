using System;
using System.IO;
using Xunit;
using AdvancedDNV;

namespace AdvancedDNV.Tests
{
    public class DNVTests : IDisposable
    {
        private readonly string testFilePath;

        public DNVTests()
        {
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

            dnv.main.Value("intVal").Set(12345);
            dnv.main.Value("strVal").Set("hello world");
            dnv.main.Value("boolVal").Set(true);
            dnv.main.Value("doubleVal").Set(3.1415926535);
            dnv.main.Value("dateVal").Set(date);
            dnv.main.Value("guidVal").Set(guid);

            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();

            Assert.Equal(12345, dnv2.main.Value("intVal").Get<int>());
            Assert.Equal("hello world", dnv2.main.Value("strVal").Get<string>());
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
            string[] strArr = new string[] { "alpha", "beta", "≥Ûdü" };

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
    }
}
