using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVContainerTests : DNVTestBase
    {
        public DNVContainerTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void NestedContainers_SetAndGet_Works()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main["level1"]["level2"]["level3"].Value("deep").Set(42);
            Assert.Equal(42, dnv.main["level1"]["level2"]["level3"].Value("deep").Get<int>());
            dnv.Close();
        }

        [Fact]
        public void NestedContainers_PersistAfterReopen()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main["a"]["b"].Value("x").Set("nested value");
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            Assert.Equal("nested value", dnv2.main["a"]["b"].Value("x").Get<string>());
            dnv2.Close();
        }

        [Fact]
        public void GetContainers_ReturnsAllChildren()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main["child1"].Value("v").Set(1);
            dnv.main["child2"].Value("v").Set(2);
            dnv.main["child3"].Value("v").Set(3);

            var containers = dnv.main.GetContainers();
            Assert.Equal(3, containers.Length);
            Assert.Contains(containers, c => c.ContainerName == "child1");
            Assert.Contains(containers, c => c.ContainerName == "child2");
            Assert.Contains(containers, c => c.ContainerName == "child3");
            dnv.Close();
        }

        [Fact]
        public void GetValues_ReturnsAllValues()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("a").Set(1);
            dnv.main.Value("b").Set(2);

            var values = dnv.main.GetValues();
            Assert.Equal(2, values.Length);
            Assert.Contains(values, v => v.ValueName == "a");
            Assert.Contains(values, v => v.ValueName == "b");
            dnv.Close();
        }

        [Fact]
        public void DropContainer_RemovesContainerInMemory()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main["toRemove"].Value("x").Set(1);
            Assert.Single(dnv.main.GetContainers());

            dnv.main["toRemove"].DropContainer();
            Assert.Empty(dnv.main.GetContainers());
            dnv.Close();
        }

        [Fact]
        public void DropContainer_RemovesAllChildrenAfterReopen()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main["keep"]["sub"].Value("v").Set("yes");
            dnv.main["remove"]["sub"].Value("v").Set("bye");
            dnv.Save();

            dnv.main["remove"].DropContainer();
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var containers = dnv2.main.GetContainers();
            Assert.DoesNotContain(containers, c => c.ContainerName == "remove");
            Assert.Contains(containers, c => c.ContainerName == "keep");
            dnv2.Close();
        }

        [Fact]
        public void ExtractToFrame_PreservesAllValues()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main["data"].Value("name").Set("Alice");
            dnv.main["data"].Value("age").Set(30);
            dnv.main["data"].Value("score").Set(9.5);

            var frame = dnv.main["data"].ExtractToFrame();
            dnv.Close();

            Assert.Equal("Alice", frame.main.Value("name").Get<string>());
            Assert.Equal(30,      frame.main.Value("age").Get<int>());
            Assert.Equal(9.5,     frame.main.Value("score").Get<double>(), 10);
        }

        [Fact]
        public void SameContainerName_SecondAccess_ReturnsSameInstance()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var c1 = dnv.main["unique"];
            c1.Value("v").Set(42);
            var c2 = dnv.main["unique"];
            // powinien zwrócić ten sam kontener (nie tworzyć nowego)
            Assert.Equal(42, c2.Value("v").Get<int>());
            dnv.Close();
        }

        [Fact]
        public void Container_ForbiddenCharInName_Throws()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            Assert.Throws<Exception>(() => _ = dnv.main["bad@name"]);
            dnv.Close();
        }

        [Fact]
        public void Value_ForbiddenCharInName_Throws()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            Assert.Throws<ArgumentException>(() => _ = dnv.main.Value("bad@name"));
            dnv.Close();
        }

        [Fact]
        public void Container_DuplicateName_ThrowsOnInsert()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            _ = dnv.main["dup"];
            // Drugi dostęp do tej samej nazwy musi zwrócić istniejący kontener — bez wyjątku
            var ex = Record.Exception(() => _ = dnv.main["dup"]);
            Assert.Null(ex);
            dnv.Close();
        }

        [Fact]
        public void DropContainer_ThenRecreate_Works()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var c = dnv.main["temp"];
            c.Value("x").Set(10);
            c.DropContainer();

            // Po usunięciu można stworzyć nowy o tej samej nazwie
            var c2 = dnv.main["temp"];
            c2.Value("x").Set(99);
            Assert.Equal(99, c2.Value("x").Get<int>());
            dnv.Close();
        }

        [Fact]
        public void Container_NameTooLong_Throws()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            string longName = new string('a', 33000); // > 32768 bytes UTF-8
            Assert.Throws<ArgumentException>(() => _ = dnv.main[longName]);
            dnv.Close();
        }
    }
}
