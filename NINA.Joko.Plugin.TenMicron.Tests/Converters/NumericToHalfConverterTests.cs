using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class NumericToHalfConverterTests {

        private readonly NumericToHalfConverter sut = new NumericToHalfConverter();

        [Test]
        public void Convert_Int_HalvesWithIntegerDivision() {
            sut.Convert(7, typeof(int), null, CultureInfo.InvariantCulture).Should().Be(3);
        }

        [Test]
        public void Convert_Long_Halves() {
            sut.Convert(100L, typeof(long), null, CultureInfo.InvariantCulture).Should().Be(50L);
        }

        [Test]
        public void Convert_Float_PreservesFraction() {
            sut.Convert(5.0f, typeof(float), null, CultureInfo.InvariantCulture).Should().Be(2.5f);
        }

        [Test]
        public void Convert_Double_PreservesFraction() {
            sut.Convert(9.0d, typeof(double), null, CultureInfo.InvariantCulture).Should().Be(4.5d);
        }

        [Test]
        public void Convert_UnsupportedType_ReturnsNull() {
            sut.Convert("string", typeof(object), null, CultureInfo.InvariantCulture).Should().BeNull();
        }
    }
}
