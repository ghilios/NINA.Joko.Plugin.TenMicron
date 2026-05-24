using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Equipment {

    [TestFixture]
    public class MountResponseParserTests {

        // ── ParseCoordinateAngle ──────────────────────────────────────────────────────────────────

        [Test]
        public void ParseCoordinateAngle_FullForm_ColonSeparator_ParsesAllFields() {
            // Matches grammar alternative #1: sign degrees ':' minutes ':' seconds '.' tenth_seconds '#'
            var result = MountResponseParser.ParseCoordinateAngle("+45:30:15.5#");

            result.Value.Positive.Should().BeTrue();
            result.Value.Degrees.Should().Be(45);
            result.Value.Minutes.Should().Be(30);
            result.Value.Seconds.Should().Be(15);
            // tenth_seconds * 10 → hundredth_seconds: 5 → 50
            result.Value.HundredthSeconds.Should().Be(50);
        }

        [Test]
        public void ParseCoordinateAngle_NegativeDegreesMinutesOnly_ParsesWithoutSeconds() {
            // Grammar alternative #3: sign degrees '*' minutes '#'
            var result = MountResponseParser.ParseCoordinateAngle("-23*26#");

            result.Value.Positive.Should().BeFalse();
            result.Value.Degrees.Should().Be(23);
            result.Value.Minutes.Should().Be(26);
            result.Value.Seconds.Should().Be(0);
            result.Value.HundredthSeconds.Should().Be(0);
        }

        [Test]
        public void ParseCoordinateAngle_AsteriskWithSeconds_Parses() {
            // Grammar alternative #2: sign degrees '*' minutes ':' seconds '#'
            var result = MountResponseParser.ParseCoordinateAngle("+12*34:56#");

            result.Value.Degrees.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
            result.Value.Seconds.Should().Be(56);
        }

        [Test]
        public void ParseCoordinateAngle_RealMountUltraPrecisionFormat_Parses() {
            // FLAG: 10Micron's ultra-precision mode (`:U2#` enabled by Mount.SetUltraPrecisionMode)
            // returns declinations as `±DD*MM:SS.S#` (asterisk separator, fractional second).
            // The Angle.g4 grammar has NO alternative for this format — only `:`-separated long form
            // (alt 1) or `*`-separated without fractional seconds (alts 2/3). This input fails
            // parsing today; the grammar needs a fourth alternative for the real mount output.
            var result = MountResponseParser.ParseCoordinateAngle("+45*30:15.5#");

            result.Value.Degrees.Should().Be(45);
            result.Value.Minutes.Should().Be(30);
            result.Value.Seconds.Should().Be(15);
            result.Value.HundredthSeconds.Should().Be(50);
        }

        // ── ParseAstrometricTime ──────────────────────────────────────────────────────────────────

        [Test]
        public void ParseAstrometricTime_FullHundredths_ParsesAllFields() {
            // FLAG (grammar ambiguity): Time.g4 has 4 alternatives that all start with `hours ':'
            // minutes ':'` and disambiguate only by the integer-field NAME after that — and ANTLR
            // can't distinguish `tenth_minutes`/`seconds`/`tenth_seconds`/`hundredth_seconds`
            // because they're all just `INTEGER`. The parser picks the first matching alternative,
            // so "12:34:56.78#" is interpreted as `seconds=56, tenth_seconds=78` (alt #3) rather
            // than `seconds=56, hundredth_seconds=78` (alt #4). Combined with Mount.cs:170-171
            // doubling, hundredths becomes 78*20=1560 today. Test asserts the *expected* parse.
            var result = MountResponseParser.ParseAstrometricTime("12:34:56.78#");

            result.Value.Hours.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
            result.Value.Seconds.Should().Be(56);
            result.Value.HundredthSeconds.Should().Be(78);
        }

        [Test]
        public void ParseAstrometricTime_TenthSecondsForm_DoesNotDoubleCount() {
            // FLAG: Mount.cs:170-171 adds `10 * tenthSeconds` twice — once into the local
            // `hundredthSeconds` variable and again in the constructor call. For "12:34:56.5#"
            // (correctly parsed by the grammar as tenth_seconds=5), expected hundredths=50;
            // current output is 100. See also the grammar-ambiguity FLAG above.
            var result = MountResponseParser.ParseAstrometricTime("12:34:56.5#");

            result.Value.Hours.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
            result.Value.Seconds.Should().Be(56);
            result.Value.HundredthSeconds.Should().Be(50);
        }

        [Test]
        public void ParseAstrometricTime_SecondsOnly_ParsesAsSecondsNotTenthMinutes() {
            // FLAG (grammar ambiguity): Time.g4 alt #1 is `hours ':' minutes ':' tenth_minutes '#'`
            // and alt #2 is `hours ':' minutes ':' seconds '#'` — both end at `#` after one INTEGER.
            // ANTLR picks alt #1 first, so "12:34:56#" is interpreted as `tenth_minutes=56` and
            // the parser code adds `seconds + 6 * 56 = 336` to seconds. Expected is `seconds=56`.
            var result = MountResponseParser.ParseAstrometricTime("12:34:56#");

            result.Value.Hours.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
            result.Value.Seconds.Should().Be(56);
            result.Value.HundredthSeconds.Should().Be(0);
        }

        // ── ParseAlignmentStarInfo ────────────────────────────────────────────────────────────────

        [Test]
        public void ParseAlignmentStarInfo_TypicalFormat_ParsesAllFields() {
            // AlignmentStarInfo.g4 uses an unambiguous form with explicit hundredthSeconds — no
            // ambiguity bug here. The declination tenthSeconds is multiplied by 10 for hundredths.
            var result = MountResponseParser.ParseAlignmentStarInfo("12:34:56.78,+45*30:15.5,12.3#");

            result.Value.LocalHour.Hours.Should().Be(12);
            result.Value.LocalHour.Minutes.Should().Be(34);
            result.Value.LocalHour.Seconds.Should().Be(56);
            result.Value.LocalHour.HundredthSeconds.Should().Be(78);
            result.Value.Declination.Positive.Should().BeTrue();
            result.Value.Declination.Degrees.Should().Be(45);
            result.Value.Declination.Minutes.Should().Be(30);
            result.Value.Declination.Seconds.Should().Be(15);
            result.Value.Declination.HundredthSeconds.Should().Be(50);
            result.Value.ErrorArcseconds.Should().Be(12.3m);
        }

        // ── ParseAlignmentModelInfo ───────────────────────────────────────────────────────────────

        [Test]
        public void ParseAlignmentModelInfo_TypicalFormat_ParsesAllFields() {
            // Format: ZZZ.ZZZZ,+AA.AAAA,EE.EEEE,PPP.PP,+OO.OOOO,+aa.aa,+bb.bb,NN,RRRRR.R#
            var result = MountResponseParser.ParseAlignmentModelInfo(
                "12.3456,+78.9012,0.1234,123.45,+0.0678,+1.50,-2.75,10,123.4#");

            result.Value.RightAscensionAzimuth.Should().Be(12.3456m);
            result.Value.RightAscensionAltitude.Should().Be(78.9012m);
            result.Value.PolarAlignErrorDegrees.Should().Be(0.1234m);
            result.Value.RightAscensionPolarPositionAngleDegrees.Should().Be(123.45m);
            result.Value.OrthogonalityErrorDegrees.Should().Be(0.0678m);
            result.Value.AzimuthAdjustmentTurns.Should().Be(1.50m);
            result.Value.AltitudeAdjustmentTurns.Should().Be(-2.75m);
            result.Value.ModelTerms.Should().Be(10);
            result.Value.RMSError.Should().Be(123.4m);
        }

        // ── ParseIP ───────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ParseIP_DHCPResponse_ParsesAllParts() {
            var result = MountResponseParser.ParseIP("192.168.1.10,255.255.255.0,192.168.1.1,D#");

            result.Value.IP.Should().Be("192.168.1.10");
            result.Value.Subnet.Should().Be("255.255.255.0");
            result.Value.Gateway.Should().Be("192.168.1.1");
            result.Value.FromDHCP.Should().BeTrue();
        }

        [Test]
        public void ParseIP_StaticResponse_FromDhcpFalse() {
            var result = MountResponseParser.ParseIP("10.0.0.5,255.255.255.0,10.0.0.1,N#");

            result.Value.IP.Should().Be("10.0.0.5");
            result.Value.FromDHCP.Should().BeFalse();
        }

        [Test]
        public void ParseIP_StripsLeadingZerosFromOctets() {
            // SanitizeIP parses each octet as int then rejoins — drops leading zeros.
            var result = MountResponseParser.ParseIP("192.168.001.010,255.255.255.000,192.168.001.001,D#");

            result.Value.IP.Should().Be("192.168.1.10");
            result.Value.Subnet.Should().Be("255.255.255.0");
        }
    }
}
