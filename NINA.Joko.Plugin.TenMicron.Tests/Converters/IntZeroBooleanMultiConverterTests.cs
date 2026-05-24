using System.Globalization;
using System.Windows;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class IntZeroBooleanMultiConverterTests {

        private readonly IntZeroBooleanMultiConverter sut = new IntZeroBooleanMultiConverter();

        [Test]
        public void Convert_EnabledTrue_ReturnsOriginalValue() {
            var values = new object[] { 42, true };

            sut.Convert(values, typeof(int), null, CultureInfo.InvariantCulture).Should().Be(42);
        }

        [Test]
        public void Convert_EnabledFalse_ReturnsZero() {
            var values = new object[] { 42, false };

            sut.Convert(values, typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }

        [Test]
        public void Convert_UnsetValue_ReturnsZero() {
            var values = new object[] { DependencyProperty.UnsetValue, true };

            sut.Convert(values, typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }

        [Test]
        public void Convert_NullValue_ReturnsZero() {
            var values = new object[] { null, true };

            sut.Convert(values, typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }

        [Test]
        public void Convert_WrongLength_ReturnsZero() {
            var values = new object[] { 42 };

            sut.Convert(values, typeof(int), null, CultureInfo.InvariantCulture).Should().Be(0);
        }
    }
}
