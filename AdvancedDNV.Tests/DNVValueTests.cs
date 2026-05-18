using System;
using Xunit;
using Xunit.Abstractions;

namespace AdvancedDNV.Tests
{
    public class DNVValueTests : DNVTestBase
    {
        public DNVValueTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void SaveAndOpen_ReadVariousTypes()
        {
            var guid = Guid.NewGuid();
            var date = new DateTime(2020, 1, 2, 3, 4, 5);

            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("intVal").Set(12345);
            dnv.main.Value("strVal").Set("hello world - polska górą ąbćżźłr@");
            dnv.main.Value("unicodeKey_ąćżźł").Set("polish");
            dnv.main.Value("boolVal").Set(true);
            dnv.main.Value("doubleVal").Set(3.1415926535);
            dnv.main.Value("dateVal").Set(date);
            dnv.main.Value("guidVal").Set(guid);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            Assert.Equal(12345,                              dnv2.main.Value("intVal").Get<int>());
            Assert.Equal("hello world - polska górą ąbćżźłr@", dnv2.main.Value("strVal").Get<string>());
            Assert.Equal("polish",                           dnv2.main.Value("unicodeKey_ąćżźł").Get<string>());
            Assert.True(dnv2.main.Value("boolVal").Get<bool>());
            Assert.Equal(3.1415926535,                       dnv2.main.Value("doubleVal").Get<double>(), 7);
            Assert.Equal(date.Ticks,                         dnv2.main.Value("dateVal").Get<DateTime>().Ticks);
            Assert.Equal(guid,                               dnv2.main.Value("guidVal").Get<Guid>());
            dnv2.Close();
        }

        [Fact]
        public void SaveAndOpen_ReadArraysAndBytes()
        {
            byte[]   byteArr = { 1, 2, 3, 255 };
            int[]    intArr  = { 10, 20, 30 };
            string[] strArr  = { "alpha", "beta", "łódź" };

            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("byteArr").Set(byteArr);
            dnv.main.Value("intArr").Set(intArr);
            dnv.main.Value("strArr").Set(strArr);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            Assert.Equal(byteArr, dnv2.main.Value("byteArr").Get<byte[]>());
            Assert.Equal(intArr,  dnv2.main.Value("intArr").Get<int[]>());
            Assert.Equal(strArr,  dnv2.main.Value("strArr").Get<string[]>());
            dnv2.Close();
        }

        [Fact]
        public void Value_IsSet_ReturnsFalse_BeforeSet()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            Assert.False(dnv.main.Value("nonExistent").IsSet());
            dnv.Close();
        }

        [Fact]
        public void Value_IsSet_ReturnsTrue_AfterSet()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("x").Set(42);
            Assert.True(dnv.main.Value("x").IsSet());
            dnv.Close();
        }

        [Fact]
        public void Value_Drop_RemovesValueInMemory()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("x").Set(99);
            Assert.True(dnv.main.Value("x").IsSet());
            dnv.main.Value("x").Drop();
            Assert.False(dnv.main.Value("x").IsSet());
            dnv.Close();
        }

        [Fact]
        public void Value_Drop_PersistsAfterReopen()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("toRemove").Set("present");
            dnv.main.Value("toKeep").Set("stays");
            dnv.Save();

            dnv.main.Value("toRemove").Drop();
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            Assert.False(dnv2.main.Value("toRemove").IsSet());
            Assert.Equal("stays", dnv2.main.Value("toKeep").Get<string>());
            dnv2.Close();
        }

        [Fact]
        public void Value_Add_IncrementsInteger()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("counter").Set(10);
            dnv.main.Value("counter").Add(5);
            Assert.Equal(15, dnv.main.Value("counter").Get<int>());
            dnv.Close();
        }

        [Fact]
        public void Value_Add_IncrementsDouble()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("score").Set(1.5);
            dnv.main.Value("score").Add(0.5);
            Assert.Equal(2.0, dnv.main.Value("score").Get<double>(), 10);
            dnv.Close();
        }

        [Fact]
        public void Value_Add_AppendsString()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("msg").Set("hello");
            dnv.main.Value("msg").Add(" world");
            Assert.Equal("hello world", dnv.main.Value("msg").Get<string>());
            dnv.Close();
        }

        [Fact]
        public void Value_Overwrite_ChangesType()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("v").Set(42);
            dnv.main.Value("v").Set("now a string");
            Assert.Equal("now a string", dnv.main.Value("v").Get<string>());
            dnv.Close();
        }

        [Fact]
        public void Value_GetWithDefault_ReturnsDefault_WhenNotSet()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            int result = dnv.main.Value("missing").Get<int>(999);
            Assert.Equal(999, result);
            dnv.Close();
        }

        [Fact]
        public void Value_Set_NullObject_ClearsValue()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("v");
            val.Set(42);
            Assert.True(val.IsSet());
            val.Set((object?)null);
            Assert.False(val.IsSet());
            dnv.Close();
        }

        [Fact]
        public void Value_Get_UnsetValue_ReturnsDefault()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("unset");
            Assert.Equal(0, val.Get<int>());
            Assert.Null(val.Get<string>());
            Assert.Equal(0.0, val.Get<double>());
            dnv.Close();
        }

        [Fact]
        public void Value_Drop_CalledTwice_DoesNotThrow()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("doubleDrop");
            val.Set(1);
            val.Drop();
            var ex = Record.Exception(() => val.Drop());
            Assert.Null(ex);
            dnv.Close();
        }

        [Fact]
        public void Value_Set_Guid_EmptyThrows()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("guidVal");
            Assert.Throws<ArgumentException>(() => val.Set(Guid.Empty));
            dnv.Close();
        }

        [Fact]
        public void Value_SetGet_TimeSpan_RoundTrip()
        {
            var ts = TimeSpan.FromHours(3.5);
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("ts").Set(ts);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var result = dnv2.main.Value("ts").Get<TimeSpan>();
            Assert.Equal(ts, result);
            dnv2.Close();
        }

        [Fact]
        public void Value_SetGet_Decimal_RoundTrip()
        {
            decimal d = 1234567.89m;
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("dec").Set(d);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var result = dnv2.main.Value("dec").Get<decimal>();
            Assert.Equal(d, result);
            dnv2.Close();
        }

        [Fact]
        public void Value_SetGet_LongArray_RoundTrip()
        {
            long[] arr = [100L, -200L, long.MaxValue];
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("la").Set(arr);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var result = dnv2.main.Value("la").Get<long[]>();
            Assert.Equal(arr, result);
            dnv2.Close();
        }

        [Fact]
        public void Value_SetGet_DoubleArray_RoundTrip()
        {
            double[] arr = [1.1, 2.2, double.MaxValue];
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("da").Set(arr);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var result = dnv2.main.Value("da").Get<double[]>();
            Assert.Equal(arr, result);
            dnv2.Close();
        }

        [Fact]
        public void Value_SetGet_ListString_RoundTrip()
        {
            var list = new System.Collections.Generic.List<string> { "alpha", "beta", "γ" };
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("ls").Set(list);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var result = dnv2.main.Value("ls").Get<System.Collections.Generic.List<string>>();
            Assert.Equal(list, result);
            dnv2.Close();
        }

        [Fact]
        public void Value_SetGet_ListInt_RoundTrip()
        {
            var list = new System.Collections.Generic.List<int> { 1, 2, 3, -999 };
            var dnv = new DNV(testFilePath);
            dnv.Open();
            dnv.main.Value("li").Set(list);
            dnv.SaveAndClose();

            var dnv2 = new DNV(testFilePath);
            dnv2.Open();
            var result = dnv2.main.Value("li").Get<System.Collections.Generic.List<int>>();
            Assert.Equal(list, result);
            dnv2.Close();
        }

        [Fact]
        public void Value_Set_SmallInt_StoredAsByte_ReadAsInt()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("small");
            val.Set(200);
            Assert.Equal(typeof(byte), val.Type);
            Assert.Equal(200, val.Get<int>());
            dnv.Close();
        }

        [Fact]
        public void Value_Set_ShortRangeInt_StoredAsShort_ReadAsInt()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("med");
            val.Set(-500);
            Assert.Equal(typeof(short), val.Type);
            Assert.Equal(-500, val.Get<int>());
            dnv.Close();
        }

        [Fact]
        public void Value_Get_NullableInt_WhenSet()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("ni");
            val.Set(42);
            int? result = val.Get<int?>();
            Assert.Equal(42, result);
            dnv.Close();
        }

        [Fact]
        public void Value_Get_NullableInt_WhenNotSet_ReturnsNull()
        {
            var dnv = new DNV(testFilePath);
            dnv.Open();
            var val = dnv.main.Value("ni2");
            int? result = val.Get<int?>();
            Assert.Null(result);
            dnv.Close();
        }
    }
}
