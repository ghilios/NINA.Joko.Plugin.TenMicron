using System;
using System.Globalization;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Converters;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Converters {

    [TestFixture]
    public class EnumStaticDescriptionTypeConverterTests {

        // ModelPointGenerationTypeEnum and ModelPointStateEnum use plain [Description("...")]
        // (not "Lbl"-prefixed), so the converter does NOT delegate to NINA.Core.Locale.Loc.Instance.
        // Loc.Instance branch deliberately not covered — same pattern as EnumStaticDescriptionValueConverterTests.
        private readonly EnumStaticDescriptionTypeConverter sut = new EnumStaticDescriptionTypeConverter(typeof(ModelPointGenerationTypeEnum));

        [Test]
        public void CanConvertTo_String_ReturnsTrue() {
            sut.CanConvertTo(null, typeof(string)).Should().BeTrue();
        }

        [Test]
        public void CanConvertFrom_String_ReturnsTrue() {
            sut.CanConvertFrom(null, typeof(string)).Should().BeTrue();
        }

        [Test]
        public void ConvertTo_StringFromEnum_ReturnsDescription() {
            var result = sut.ConvertTo(null, CultureInfo.InvariantCulture, ModelPointGenerationTypeEnum.GoldenSpiral, typeof(string));

            result.Should().Be("Golden Spiral");
        }

        [Test]
        public void ConvertTo_StringFromAnotherEnumValue_ReturnsDescription() {
            var result = sut.ConvertTo(null, CultureInfo.InvariantCulture, ModelPointGenerationTypeEnum.SiderealPath, typeof(string));

            result.Should().Be("Sidereal Path");
        }

        [Test]
        public void ConvertTo_StringFromNull_ReturnsEmpty() {
            var result = sut.ConvertTo(null, CultureInfo.InvariantCulture, null, typeof(string));

            result.Should().Be(string.Empty);
        }

        [Test]
        public void ConvertFrom_DescriptionString_ReturnsEnumValue() {
            // Base EnumConverter parses by enum member name (e.g. "GoldenSpiral"), not by Description.
            var result = sut.ConvertFrom(null, CultureInfo.InvariantCulture, "GoldenSpiral");

            result.Should().Be(ModelPointGenerationTypeEnum.GoldenSpiral);
        }
    }
}
