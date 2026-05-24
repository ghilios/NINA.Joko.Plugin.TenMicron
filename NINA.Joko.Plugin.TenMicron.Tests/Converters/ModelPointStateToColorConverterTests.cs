using System.Globalization;
using System.Windows.Media;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class ModelPointStateToColorConverterTests {

        private readonly ModelPointStateToColorConverter sut = new ModelPointStateToColorConverter();

        [TestCase(ModelPointStateEnum.Generated, "LightGreen")]
        [TestCase(ModelPointStateEnum.Failed, "Red")]
        [TestCase(ModelPointStateEnum.UpNext, "YellowGreen")]
        [TestCase(ModelPointStateEnum.Exposing, "LightBlue")]
        [TestCase(ModelPointStateEnum.Processing, "Blue")]
        public void Convert_MappedStates_ReturnExpectedColors(ModelPointStateEnum state, string expectedColorName) {
            var color = (Color)sut.Convert(state, typeof(Color), null, CultureInfo.InvariantCulture);

            var expected = (Color)typeof(Colors).GetProperty(expectedColorName).GetValue(null);
            color.Should().Be(expected);
        }

        [Test]
        public void Convert_UnmappedState_ReturnsBlack() {
            // AddedToModel and the bounds states all fall through to Colors.Black.
            var color = (Color)sut.Convert(ModelPointStateEnum.AddedToModel, typeof(Color), null, CultureInfo.InvariantCulture);

            color.Should().Be(Colors.Black);
        }

        [Test]
        public void Convert_NonEnum_ReturnsBlack() {
            var color = (Color)sut.Convert("not-an-enum", typeof(Color), null, CultureInfo.InvariantCulture);

            color.Should().Be(Colors.Black);
        }
    }
}
