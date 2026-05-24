using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class DecimalDegreesToArcsecDoubleDashConverterTests {

        private readonly DecimalDegreesToArcsecDoubleDashConverter sut = new DecimalDegreesToArcsecDoubleDashConverter();

        [Test]
        public void Convert_DecimalMinValue_ReturnsDoubleDash() {
            sut.Convert(decimal.MinValue, typeof(object), null, CultureInfo.InvariantCulture).Should().Be("--");
        }

        [Test]
        public void Convert_Decimal_MultipliesBy3600() {
            sut.Convert(1.0m, typeof(object), null, CultureInfo.InvariantCulture).Should().Be(3600m);
        }

        [Test]
        public void Convert_NonDecimal_ReturnsValueAsIs() {
            sut.Convert("anything", typeof(object), null, CultureInfo.InvariantCulture).Should().Be("anything");
        }

        [Test]
        public void ConvertBack_DoubleDash_ReturnsDecimalMinValue() {
            sut.ConvertBack("--", typeof(decimal), null, CultureInfo.InvariantCulture).Should().Be(decimal.MinValue);
        }

        [Test]
        public void ConvertBack_NumericString_DividesBy3600() {
            sut.ConvertBack("3600", typeof(decimal), null, CultureInfo.InvariantCulture).Should().Be(1.0m);
        }
    }
}
