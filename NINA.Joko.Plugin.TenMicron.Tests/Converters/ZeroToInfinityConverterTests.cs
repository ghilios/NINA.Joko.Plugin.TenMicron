using System;
using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class ZeroToInfinityConverterTests {

        private readonly ZeroToInfinityConverter sut = new ZeroToInfinityConverter();

        [Test]
        public void Convert_IntZero_ReturnsUnlimited() {
            sut.Convert(0, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("unlimited");
        }

        [Test]
        public void Convert_PositiveInt_ReturnsItsString() {
            sut.Convert(7, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("7");
        }

        [Test]
        public void Convert_DoubleZero_ReturnsUnlimited() {
            sut.Convert(0.0d, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("unlimited");
        }

        [Test]
        public void Convert_DoubleNaN_ReturnsUnlimited() {
            sut.Convert(double.NaN, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("unlimited");
        }

        [Test]
        public void Convert_NegativeInt_ReturnsUnlimited() {
            sut.Convert(-3, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("unlimited");
        }

        [Test]
        public void Convert_InvalidType_Throws() {
            Action act = () => sut.Convert("bad", typeof(string), null, CultureInfo.InvariantCulture);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void ConvertBack_UnlimitedToInt_ReturnsZero() {
            sut.ConvertBack("unlimited", typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }

        [Test]
        public void ConvertBack_NumericStringToInt_ReturnsParsed() {
            sut.ConvertBack("12", typeof(int), null, CultureInfo.InvariantCulture).Should().Be(12);
        }

        [Test]
        public void ConvertBack_NegativeStringToInt_ReturnsZero() {
            sut.ConvertBack("-1", typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }

        [Test]
        public void ConvertBack_UnlimitedToDouble_ReturnsZero() {
            sut.ConvertBack("unlimited", typeof(double), null, CultureInfo.InvariantCulture).Should().Be(0.0d);
        }
    }
}
