using System;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVMetadataTests : DNVTestBase
    {
        public DNVMetadataTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Metadata_AfterOpen_DefaultValues()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            Assert.False(dnv.Meta.isCompressed);
            Assert.Equal(0L, dnv.Meta.numberOfOpening);
            Assert.Equal(0L, dnv.Meta.numberOfSaving);
            dnv.Close();
        }

        [Fact]
        public void Metadata_SaveCounter_IncrementsOnEachSave()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("v").Set(1);
            dnv.Save();
            dnv.Save();                 // bez zmian — pomijany
            dnv.main.Value("v").Set(2);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            // Liczą się tylko zapisy, przed którymi coś się zmieniło: pierwszy Save i SaveAndClose
            Assert.Equal(2L, dnv2.Meta.numberOfSaving);
            dnv2.Close();
        }

        [Fact]
        public void Metadata_Author_SetOnFirstSave()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("v").Set(1);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            Assert.NotEmpty(dnv2.Meta.author);
            Assert.NotEqual("N/A", dnv2.Meta.author);
            dnv2.Close();
        }

        [Fact]
        public void Metadata_Created_IsSet_AfterFirstSave()
        {
            var before = DateTime.UtcNow.AddSeconds(-1);

            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("v").Set(1);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            Assert.True(dnv2.Meta.created >= before);
            dnv2.Close();
        }

        [Fact]
        public void Metadata_Throws_WhenNotOpened()
        {
            var dnv = new DNV(testFilePath);
            Assert.Throws<Exception>(() => _ = dnv.Meta.numberOfSaving);
        }

        [Fact]
        public void Metadata_NumberOfOpening_IncrementsEachOpen()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            long openings = dnv2.Meta.numberOfOpening;
            dnv2.Close();
            Assert.True(openings >= 1);
        }
    }
}
