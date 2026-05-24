using System;
using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class ZeroToDisabledConverterTests {

        private readonly ZeroToDisabledConverter sut = new ZeroToDisabledConverter();

        [Test]
        public void Convert_Zero_ReturnsDisabled() {
            sut.Convert(0, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("disabled");
        }

        [Test]
        public void Convert_PositiveInt_ReturnsItsString() {
            sut.Convert(5, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("5");
        }

        [Test]
        public void Convert_NegativeInt_ReturnsDisabled() {
            sut.Convert(-2, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("disabled");
        }

        [Test]
        public void Convert_NonInt_Throws() {
            Action act = () => sut.Convert(1.5, typeof(string), null, CultureInfo.InvariantCulture);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void ConvertBack_DisabledString_ReturnsZero() {
            sut.ConvertBack("disabled", typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }

        [Test]
        public void ConvertBack_PositiveString_ReturnsParsed() {
            sut.ConvertBack("42", typeof(int), null, CultureInfo.InvariantCulture).Should().Be(42);
        }

        [Test]
        public void ConvertBack_NegativeString_ClampedToZero() {
            sut.ConvertBack("-7", typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }
    }
}
