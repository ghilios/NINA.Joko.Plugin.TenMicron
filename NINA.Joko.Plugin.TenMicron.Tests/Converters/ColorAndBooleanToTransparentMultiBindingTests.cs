using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class ColorAndBooleanToTransparentMultiBindingTests {

        private readonly ColorAndBooleanToTransparentMultiBinding sut = new ColorAndBooleanToTransparentMultiBinding();

        [Test]
        public void Convert_EnabledTrue_ReturnsOriginalColor() {
            var color = Colors.Red;

            var result = sut.Convert(new object[] { color, true }, typeof(Color), null, CultureInfo.InvariantCulture);

            result.Should().Be(color);
        }

        [Test]
        public void Convert_EnabledFalse_ReturnsTransparent() {
            var result = sut.Convert(new object[] { Colors.Red, false }, typeof(Color), null, CultureInfo.InvariantCulture);

            result.Should().Be(Colors.Transparent);
        }

        [Test]
        public void Convert_NullColor_ReturnsTransparent() {
            var result = sut.Convert(new object[] { null, true }, typeof(Color), null, CultureInfo.InvariantCulture);

            result.Should().Be(Colors.Transparent);
        }

        [Test]
        public void Convert_UnsetColor_ReturnsTransparent() {
            var result = sut.Convert(new object[] { DependencyProperty.UnsetValue, true }, typeof(Color), null, CultureInfo.InvariantCulture);

            result.Should().Be(Colors.Transparent);
        }

        [Test]
        public void Convert_WrongLength_ReturnsTransparent() {
            var result = sut.Convert(new object[] { Colors.Red }, typeof(Color), null, CultureInfo.InvariantCulture);

            result.Should().Be(Colors.Transparent);
        }

        [Test]
        public void ConvertBack_Throws_NotImplemented() {
            // Cast to the explicitly-implemented interface to reach ConvertBack.
            System.Windows.Data.IMultiValueConverter mvc = sut;
            Action act = () => mvc.ConvertBack(Colors.Red, new[] { typeof(Color), typeof(bool) }, null, CultureInfo.InvariantCulture);

            act.Should().Throw<NotImplementedException>();
        }
    }
}
