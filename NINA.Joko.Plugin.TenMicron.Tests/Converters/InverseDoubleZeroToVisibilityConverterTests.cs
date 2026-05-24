using System;
using System.Globalization;
using System.Windows;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class InverseDoubleZeroToVisibilityConverterTests {

        private readonly InverseDoubleZeroToVisibilityConverter sut = new InverseDoubleZeroToVisibilityConverter();

        [Test]
        public void Convert_DoubleZero_ReturnsVisible() {
            sut.Convert(0.0d, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [Test]
        public void Convert_DoubleNonZero_ReturnsCollapsed() {
            sut.Convert(1.0d, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_DoubleTinyButWithinTolerance_ReturnsVisible() {
            // Within the 0.00001 tolerance.
            sut.Convert(1e-6d, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [TestCase(0.0)]
        [TestCase(0)]
        public void Convert_DecimalAndIntZero_ReturnsVisible(double raw) {
            // Cover the decimal and int branches explicitly.
            sut.Convert((decimal)raw, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
            sut.Convert((int)raw, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [Test]
        public void Convert_FloatNonZero_ReturnsCollapsed() {
            sut.Convert(1.0f, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_Null_ReturnsVisible() {
            sut.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [Test]
        public void Convert_InvalidType_Throws() {
            Action act = () => sut.Convert("bad", typeof(Visibility), null, CultureInfo.InvariantCulture);

            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void ConvertBack_Throws_NotImplemented() {
            Action act = () => sut.ConvertBack(Visibility.Visible, typeof(double), null, CultureInfo.InvariantCulture);

            act.Should().Throw<NotImplementedException>();
        }
    }
}
