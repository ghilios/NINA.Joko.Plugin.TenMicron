using System;
using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class EnumStaticDescriptionValueConverterTests {

        private readonly EnumStaticDescriptionValueConverter sut = new EnumStaticDescriptionValueConverter();

        [Test]
        public void Convert_EnumWithDescription_ReturnsDescription() {
            // ModelPointGenerationTypeEnum.GoldenSpiral has Description("Golden Spiral") — non-"Lbl" prefix
            // so the converter does NOT delegate to NINA.Core.Locale.Loc.Instance.
            var result = sut.Convert(ModelPointGenerationTypeEnum.GoldenSpiral, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("Golden Spiral");
        }

        [Test]
        public void Convert_AnotherEnumValue_ReturnsItsDescription() {
            var result = sut.Convert(ModelPointStateEnum.OutsideAzimuthBounds, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("Outside Azimuth Bounds");
        }

        [Test]
        public void Convert_NonStringTargetType_Throws() {
            Action act = () => sut.Convert(ModelPointGenerationTypeEnum.GoldenSpiral, typeof(int), null, CultureInfo.InvariantCulture);

            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void Convert_Null_ReturnsEmpty() {
            var result = sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(string.Empty);
        }
    }
}
