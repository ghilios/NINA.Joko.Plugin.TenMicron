using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class PositiveToBooleanConverterTests {

        private readonly PositiveToBooleanConverter sut = new PositiveToBooleanConverter();

        [Test]
        public void Convert_PositiveInt_ReturnsTrue() {
            sut.Convert(1, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(true);
        }

        [Test]
        public void Convert_Zero_ReturnsFalse() {
            sut.Convert(0, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
        }

        [Test]
        public void Convert_NegativeInt_ReturnsFalse() {
            sut.Convert(-1, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
        }

        [Test]
        public void Convert_NonInt_ReturnsFalse() {
            sut.Convert("string", typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
        }
    }
}
