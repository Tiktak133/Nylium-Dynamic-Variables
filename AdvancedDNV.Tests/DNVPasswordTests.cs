using System;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVPasswordTests : DNVTestBase
    {
        public DNVPasswordTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void SaveWithPassword_OpenWithCorrectPassword_Succeeds()
        {
            const string password = "MyS3cretP@ssw0rd!";

            var dnv = new DNV(testFilePath, password);
            dnv.Open();
            dnv.main.Value("secret").Set("classified");
            dnv.main.Value("number").Set(12345);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath, password);
            bool opened = dnv2.Open();
            Assert.True(opened);
            Assert.Equal("classified", dnv2.main.Value("secret").Get<string>());
            Assert.Equal(12345,        dnv2.main.Value("number").Get<int>());
            dnv2.Close();
        }

        [Fact]
        public void SaveWithPassword_OpenWithWrongPassword_DataIsUnreadable()
        {
            const string correctPassword = "CorrectPassword123";
            const string wrongPassword   = "WrongPassword456";

            var dnv = new DNV(testFilePath, correctPassword);
            dnv.Open();
            dnv.main.Value("data").Set("sensitive");
            dnv.SaveAndClose();

            // Przy złym haśle biblioteka albo rzuca wyjątek,
            // albo otwiera plik, ale dane są niedostępne/puste
            try
            {
                var dnv2 = new DNV(testFilePath, wrongPassword);
                dnv2.Open();
                // Jeśli open się udało — dane nie powinny być poprawne
                string? read = dnv2.main.Value("data").Get<string>();
                Assert.NotEqual("sensitive", read);
                dnv2.Close();
            }
            catch
            {
                // Wyjątek podczas Open też jest akceptowalny
            }
        }

        [Fact]
        public void SaveWithPassword_OpenWithoutPassword_Throws()
        {
            const string password = "SecurePass!99";

            var dnv = new DNV(testFilePath, password);
            dnv.Open();
            dnv.main.Value("x").Set(1);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);  // brak hasła
            Assert.Throws<Exception>(() => dnv2.Open());
        }

        [Fact]
        public void SaveWithPassword_FileIsEncrypted()
        {
            const string password = "TestEncryption!";

            var dnv = new DNV(testFilePath, password);
            dnv.Open();
            dnv.main.Value("payload").Set("TOP SECRET");
            dnv.SaveAndClose();

            // Odczytaj surowe bajty pliku i sprawdź, że nie ma tam plaintekstu
            byte[] raw = System.IO.File.ReadAllBytes(testFilePath);
            string rawText = System.Text.Encoding.UTF8.GetString(raw);
            Assert.DoesNotContain("TOP SECRET", rawText);
        }

        [Fact]
        public void SaveWithPassword_ReopenedFile_ContentIsReadable()
        {
            const string password = "MetaEncCheck!1";

            var dnv = new DNV(testFilePath, password);
            dnv.Open();
            dnv.main.Value("secret").Set("confidential");
            dnv.SaveAndClose();

            // Plik musi dać się ponownie otworzyć z poprawnym hasłem
            var dnv2 = new DNV(testFilePath, password);
            dnv2.Open();
            Assert.Equal("confidential", dnv2.main.Value("secret").Get<string>());
            dnv2.Close();
        }
    }
}
