using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class DecimalMinToDoubleDashConverterTests {

        private readonly DecimalMinToDoubleDashConverter sut = new DecimalMinToDoubleDashConverter();

        [Test]
        public void Convert_DecimalMinValue_ReturnsDoubleDash() {
            sut.Convert(decimal.MinValue, typeof(object), null, CultureInfo.InvariantCulture).Should().Be("--");
        }

        [Test]
        public void Convert_OtherDecimal_PassesThrough() {
            sut.Convert(5.0m, typeof(object), null, CultureInfo.InvariantCulture).Should().Be(5.0m);
        }

        [Test]
        public void Convert_NonDecimal_PassesThrough() {
            sut.Convert("hello", typeof(object), null, CultureInfo.InvariantCulture).Should().Be("hello");
        }

        [Test]
        public void ConvertBack_DoubleDash_ReturnsDecimalMinValue() {
            sut.ConvertBack("--", typeof(decimal), null, CultureInfo.InvariantCulture).Should().Be(decimal.MinValue);
        }

        [Test]
        public void ConvertBack_OtherString_PassesThrough() {
            sut.ConvertBack("3.14", typeof(decimal), null, CultureInfo.InvariantCulture).Should().Be("3.14");
        }
    }
}
