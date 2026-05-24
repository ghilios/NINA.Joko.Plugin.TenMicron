using System;
using System.Globalization;
using System.Windows;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class DoubleZeroToVisibilityConverterTests {

        private readonly DoubleZeroToVisibilityConverter sut = new DoubleZeroToVisibilityConverter();

        [Test]
        public void Convert_DoubleZero_ReturnsCollapsed() {
            sut.Convert(0.0d, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_DoubleNonZero_ReturnsVisible() {
            sut.Convert(1.0d, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [Test]
        public void Convert_DoubleTinyButNonZero_ReturnsCollapsed() {
            // Within the 0.00001 tolerance.
            sut.Convert(1e-6d, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_DecimalZero_ReturnsCollapsed() {
            sut.Convert(0.0m, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_FloatZero_ReturnsCollapsed() {
            sut.Convert(0.0f, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_Null_ReturnsCollapsed() {
            sut.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_InvalidType_Throws() {
            Action act = () => sut.Convert("string", typeof(Visibility), null, CultureInfo.InvariantCulture);

            act.Should().Throw<ArgumentException>();
        }
    }
}
