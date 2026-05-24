using System;
using System.Globalization;
using System.Windows;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class IntNegativeToVisibilityConverterTests {

        private readonly IntNegativeToVisibilityConverter sut = new IntNegativeToVisibilityConverter();

        [Test]
        public void Convert_NegativeInt_ReturnsCollapsed() {
            sut.Convert(-1, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_Zero_ReturnsVisible() {
            sut.Convert(0, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [Test]
        public void Convert_PositiveInt_ReturnsVisible() {
            sut.Convert(42, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Visible);
        }

        [Test]
        public void Convert_Null_ReturnsCollapsed() {
            sut.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        }

        [Test]
        public void Convert_InvalidType_Throws() {
            Action act = () => sut.Convert("bad", typeof(Visibility), null, CultureInfo.InvariantCulture);

            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void ConvertBack_Throws_NotImplemented() {
            Action act = () => sut.ConvertBack(Visibility.Visible, typeof(int), null, CultureInfo.InvariantCulture);

            act.Should().Throw<NotImplementedException>();
        }
    }
}
