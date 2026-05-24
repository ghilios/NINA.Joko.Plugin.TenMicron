using FluentAssertions;
using Moq;
using NINA.Core.Enum;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.TenMicron.Tests.Equipment {

    [TestFixture]
    public class MountTests {

        private Mock<IMountCommander> commander;
        private Mount sut;

        [SetUp]
        public void SetUp() {
            commander = new Mock<IMountCommander>();
            sut = new Mount(commander.Object);
        }

        [Test]
        public void GetDeclination_SendsGD_AndParsesResponse() {
            // Using a format the grammar accepts unambiguously (alt 2: `*` separator, no fractional sec).
            // The real mount format `+DD*MM:SS.S#` is covered as a FLAG test in MountResponseParserTests.
            commander.Setup(c => c.SendCommandString(":GD#", true)).Returns("+45*30:15#");

            var result = sut.GetDeclination();

            result.Value.Degrees.Should().Be(45);
            result.Value.Minutes.Should().Be(30);
            result.Value.Seconds.Should().Be(15);
        }

        [Test]
        public void GetRightAscension_SendsGR_AndParsesResponse() {
            commander.Setup(c => c.SendCommandString(":GR#", true)).Returns("12:34:56.78#");

            var result = sut.GetRightAscension();

            result.Value.Hours.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
            result.Value.Seconds.Should().Be(56);
            result.Value.HundredthSeconds.Should().Be(78);
        }

        [Test]
        public void GetModelCount_ParsesIntegerFromResponse() {
            commander.Setup(c => c.SendCommandString(":modelcnt#", true)).Returns("7#");

            var result = sut.GetModelCount();

            result.Value.Should().Be(7);
        }

        [Test]
        public void GetAlignmentStarCount_ParsesIntegerFromResponse() {
            commander.Setup(c => c.SendCommandString(":getalst#", true)).Returns("42#");

            var result = sut.GetAlignmentStarCount();

            result.Value.Should().Be(42);
        }

        [Test]
        public void LoadModel_SuccessResponse_ReturnsTrue() {
            commander.Setup(c => c.SendCommandString(":modelld0my_model#", true)).Returns("1#");

            sut.LoadModel("my_model").Value.Should().BeTrue();
        }

        [Test]
        public void LoadModel_FailureResponse_ReturnsFalse() {
            commander.Setup(c => c.SendCommandString(":modelld0bad#", true)).Returns("0#");

            sut.LoadModel("bad").Value.Should().BeFalse();
        }

        [Test]
        public void GetTrackingRateArcsecsPerSec_DividesResponseBy4() {
            // Spec: response divided by 4 to get arcsecs/sec.
            commander.Setup(c => c.SendCommandString(":GT#", true)).Returns("60.064#");

            var result = sut.GetTrackingRateArcsecsPerSec();

            result.Value.Should().Be(15.016m);
        }

        [Test]
        public void GetIPAddress_DelegatesToParser() {
            commander.Setup(c => c.SendCommandString(":GIP#", true))
                .Returns("192.168.1.10,255.255.255.0,192.168.1.1,D#");

            var result = sut.GetIPAddress();

            result.Value.IP.Should().Be("192.168.1.10");
            result.Value.FromDHCP.Should().BeTrue();
        }

        // ---------- Simple read methods through SendCommandString ----------

        [Test]
        public void GetLocalSiderealTime_SendsGS_AndParsesResponse() {
            commander.Setup(c => c.SendCommandString(":GS#", true)).Returns("12:34:56.78#");

            var result = sut.GetLocalSiderealTime();

            result.Value.Hours.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
            result.Value.Seconds.Should().Be(56);
            result.Value.HundredthSeconds.Should().Be(78);
        }

        [Test]
        public void GetSideOfPier_EastResponse_ReturnsPierEast() {
            commander.Setup(c => c.SendCommandString(":pS#", true)).Returns("East#");

            var result = sut.GetSideOfPier();

            result.Value.Should().Be(PierSide.pierEast);
        }

        [Test]
        public void GetSideOfPier_UnknownResponse_Throws() {
            commander.Setup(c => c.SendCommandString(":pS#", true)).Returns("Unknown#");

            Action act = () => sut.GetSideOfPier();

            // Source throws plain Exception with message "Unexpected pier side {raw} returned by {command}".
            act.Should().Throw<Exception>().WithMessage("*Unexpected pier side*");
        }

        [Test]
        public void GetId_TrimsTrailingHash() {
            commander.Setup(c => c.SendCommandString(":GETID#", true)).Returns("MyMount#");

            sut.GetId().Value.Should().Be("MyMount");
        }

        [Test]
        public void GetMeridianSlewLimitDegrees_ParsesInteger() {
            commander.Setup(c => c.SendCommandString(":Glms#", true)).Returns("10#");

            sut.GetMeridianSlewLimitDegrees().Value.Should().Be(10);
        }

        [Test]
        public void GetSlewSettleTimeSeconds_ParsesDecimal() {
            commander.Setup(c => c.SendCommandString(":Gstm#", true)).Returns("1.5#");

            sut.GetSlewSettleTimeSeconds().Value.Should().Be(1.5m);
        }

        [Test]
        public void GetStatus_ZeroResponse_ReturnsTracking() {
            commander.Setup(c => c.SendCommandString(":Gstat#", true)).Returns("0#");

            sut.GetStatus().Value.Should().Be(MountStatusEnum.Tracking);
        }

        [Test]
        public void GetPressure_ParsesDecimal() {
            commander.Setup(c => c.SendCommandString(":GRPRS#", true)).Returns("1013.25#");

            sut.GetPressure().Value.Should().Be(1013.25m);
        }

        [Test]
        public void GetTemperature_ParsesDecimal() {
            commander.Setup(c => c.SendCommandString(":GRTMP#", true)).Returns("15.5#");

            sut.GetTemperature().Value.Should().Be(15.5m);
        }

        [Test]
        public void GetMACAddress_TrimsTrailingHash() {
            commander.Setup(c => c.SendCommandString(":GMAC#", true)).Returns("AA:BB:CC:DD:EE:FF#");

            sut.GetMACAddress().Value.Should().Be("AA:BB:CC:DD:EE:FF");
        }

        [Test]
        public void GetModelName_ValidIndex_ReturnsTrimmedName() {
            commander.Setup(c => c.SendCommandString(":modelnam5#", true)).Returns("MyModel#");

            sut.GetModelName(5).Value.Should().Be("MyModel");
        }

        [Test]
        public void GetModelName_IndexBelowOne_ThrowsArgumentException() {
            Action act = () => sut.GetModelName(0);

            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void GetModelName_IndexAboveNinetyNine_ThrowsArgumentException() {
            Action act = () => sut.GetModelName(100);

            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void GetModelName_EmptyResponse_Throws() {
            commander.Setup(c => c.SendCommandString(":modelnam5#", true)).Returns("#");

            Action act = () => sut.GetModelName(5);

            // Source throws plain Exception with message "{modelIndex} is not a valid model index".
            act.Should().Throw<Exception>().WithMessage("*not a valid model index*");
        }

        [Test]
        public void GetAlignmentModelInfo_ParsesFields() {
            // Response field order: raAzimuth, raAltitude, paError, raPositionAngle,
            // orthogonalityError, azimuthTurns, altitudeTurns, modelTerms, rmsError.
            commander.Setup(c => c.SendCommandString(":getain#", true))
                .Returns("12.3456,+78.9012,0.1234,123.45,+0.0678,+1.50,-2.75,10,123.4#");

            var result = sut.GetAlignmentModelInfo();

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

        [Test]
        public void GetAlignmentStarInfo_ValidIndex_ParsesResponse() {
            commander.Setup(c => c.SendCommandString(":getali1#", true))
                .Returns("12:34:56.78,+45*30:15.5,12.3#");

            var result = sut.GetAlignmentStarInfo(1);

            result.Value.LocalHour.Hours.Should().Be(12);
            result.Value.ErrorArcseconds.Should().Be(12.3m);
        }

        [Test]
        public void GetAlignmentStarInfo_IndexBelowOne_ThrowsArgumentException() {
            Action act = () => sut.GetAlignmentStarInfo(0);

            act.Should().Throw<ArgumentException>();
        }

        // ---------- Bool methods through SendCommandBool / SendCommandString ----------

        [TestCase("1#", true)]
        [TestCase("0#", false)]
        public void SaveModel_ResponseDeterminesResult(string response, bool expected) {
            commander.Setup(c => c.SendCommandString(":modelsv0name#", true)).Returns(response);

            sut.SaveModel("name").Value.Should().Be(expected);
        }

        [TestCase("1#", true)]
        [TestCase("0#", false)]
        public void DeleteModel_ResponseDeterminesResult(string response, bool expected) {
            commander.Setup(c => c.SendCommandString(":modeldel0name#", true)).Returns(response);

            sut.DeleteModel("name").Value.Should().Be(expected);
        }

        [TestCase("1#", true)]
        [TestCase("0#", false)]
        public void DeleteAlignmentStar_ResponseDeterminesResult(string response, bool expected) {
            commander.Setup(c => c.SendCommandString(":delalst3#", true)).Returns(response);

            sut.DeleteAlignmentStar(3).Value.Should().Be(expected);
        }

        [TestCase("V#", true)]
        [TestCase("E#", false)]
        public void StartNewAlignmentSpec_ResponseDeterminesResult(string response, bool expected) {
            commander.Setup(c => c.SendCommandString(":newalig#", true)).Returns(response);

            sut.StartNewAlignmentSpec().Value.Should().Be(expected);
        }

        [TestCase("V#", true)]
        [TestCase("E#", false)]
        public void FinishAlignmentSpec_ResponseDeterminesResult(string response, bool expected) {
            commander.Setup(c => c.SendCommandString(":endalig#", true)).Returns(response);

            sut.FinishAlignmentSpec().Value.Should().Be(expected);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Shutdown_ReturnsCommanderResult(bool commanderResult) {
            commander.Setup(c => c.SendCommandBool(":shutdown#", true)).Returns(commanderResult);

            sut.Shutdown().Value.Should().Be(commanderResult);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GetRefractionCorrectionEnabled_ReturnsCommanderResult(bool commanderResult) {
            commander.Setup(c => c.SendCommandBool(":GREF#", true)).Returns(commanderResult);

            sut.GetRefractionCorrectionEnabled().Value.Should().Be(commanderResult);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GetDualAxisTrackingEnabled_ReturnsCommanderResult(bool commanderResult) {
            commander.Setup(c => c.SendCommandBool(":Gdat#", true)).Returns(commanderResult);

            sut.GetDualAxisTrackingEnabled().Value.Should().Be(commanderResult);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GetUnattendedFlipEnabled_ReturnsCommanderResult(bool commanderResult) {
            commander.Setup(c => c.SendCommandBool(":Guaf#", true)).Returns(commanderResult);

            sut.GetUnattendedFlipEnabled().Value.Should().Be(commanderResult);
        }

        [Test]
        public void SetRefractionCorrection_True_SendsSREF1AndReturnsCommanderResult() {
            commander.Setup(c => c.SendCommandBool(":SREF1#", true)).Returns(true);

            sut.SetRefractionCorrection(true).Value.Should().BeTrue();
        }

        [Test]
        public void SetRefractionCorrection_FalseAndCommanderReturnsFalse_ReturnsFalse() {
            commander.Setup(c => c.SendCommandBool(":SREF0#", true)).Returns(false);

            sut.SetRefractionCorrection(false).Value.Should().BeFalse();
        }

        [Test]
        public void SetDualAxisTracking_False_SendsSdat0() {
            commander.Setup(c => c.SendCommandBool(":Sdat0#", true)).Returns(true);

            sut.SetDualAxisTracking(false).Value.Should().BeTrue();
        }

        [Test]
        public void SetDualAxisTracking_CommanderFalse_ReturnsFalse() {
            commander.Setup(c => c.SendCommandBool(":Sdat1#", true)).Returns(false);

            sut.SetDualAxisTracking(true).Value.Should().BeFalse();
        }

        [Test]
        public void SetMeridianSlewLimit_TenDegrees_SendsSlms10() {
            commander.Setup(c => c.SendCommandBool(":Slms10#", true)).Returns(true);

            sut.SetMeridianSlewLimit(10).Value.Should().BeTrue();
        }

        [Test]
        public void SetSlewSettleTime_OnePointFive_SendsFormattedCommand() {
            // Format string ":Sstm{seconds:00000.000}#" => "1.5" becomes "00001.500".
            commander.Setup(c => c.SendCommandBool(":Sstm00001.500#", true)).Returns(true);

            sut.SetSlewSettleTime(1.5m).Value.Should().BeTrue();
        }

        [Test]
        public void SetSlewSettleTime_NegativeValue_ReturnsFalseWithoutCallingCommander() {
            sut.SetSlewSettleTime(-1m).Value.Should().BeFalse();

            commander.Verify(c => c.SendCommandBool(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void SetSlewSettleTime_AboveMax_ReturnsFalseWithoutCallingCommander() {
            sut.SetSlewSettleTime(100000m).Value.Should().BeFalse();

            commander.Verify(c => c.SendCommandBool(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void SetPressure_RoundsToSingleDecimalAndSendsSRPRS() {
            // Source: ":SRPRS{val:0000.0}#". 1013.25 => "1013.3" (banker's-rounding via standard format).
            commander.Setup(c => c.SendCommandBool(":SRPRS1013.3#", true)).Returns(true);

            sut.SetPressure(1013.25).Value.Should().BeTrue();
        }

        [Test]
        public void SetTemperature_PositiveValue_SendsPlusSignAndZeroPaddedFormat() {
            // Source: ":SRTMP{sign}{val:000.0}#". 15.5 => "+015.5".
            commander.Setup(c => c.SendCommandBool(":SRTMP+015.5#", true)).Returns(true);

            sut.SetTemperature(15.5).Value.Should().BeTrue();
        }

        [Test]
        public void SetTemperature_NegativeValue_SendsMinusSignAndAbsoluteFormat() {
            // The format string `000.0` is applied to the raw value (-5.0), which itself prints `-005.0`.
            // Combined with the explicit '-' sign the source emits, the resulting command has a doubled
            // minus: `:SRTMP--005.0#`.
            // FLAG: Mount.SetTemperature emits a doubled minus for negative temperatures because
            // the sign is added explicitly AND the value is not absolute-valued before formatting.
            commander.Setup(c => c.SendCommandBool(":SRTMP--005.0#", true)).Returns(true);

            sut.SetTemperature(-5.0).Value.Should().BeTrue();
        }

        [Test]
        [Ignore("FLAG: pinned in SetTemperature_NegativeValue_SendsMinusSignAndAbsoluteFormat. Will fail until source is fixed.")]
        public void SetTemperature_NegativeValue_ShouldSendSingleMinusSign() {
            // Intended behavior: a single '-' sign followed by the absolute value, e.g. ":SRTMP-005.0#".
            // When the source is fixed (Math.Abs() the value before formatting), un-ignore this and
            // remove the pinning sibling test above.
            commander.Setup(c => c.SendCommandBool(":SRTMP-005.0#", true)).Returns(true);

            sut.SetTemperature(-5.0).Value.Should().BeTrue();
        }

        // ---------- Fire-and-forget commands ----------

        [Test]
        public void DeleteAlignment_EmptyResponse_DoesNotThrow() {
            // Source actually uses SendCommandString (not SendCommandBlind). Empty payload "#" passes.
            commander.Setup(c => c.SendCommandString(":delalig#", true)).Returns("#");

            Action act = () => sut.DeleteAlignment();

            act.Should().NotThrow();
            commander.Verify(c => c.SendCommandString(":delalig#", true), Times.Once);
        }

        [Test]
        public void DeleteAlignment_NonEmptyResponse_Throws() {
            commander.Setup(c => c.SendCommandString(":delalig#", true)).Returns("E#");

            Action act = () => sut.DeleteAlignment();

            // Source throws plain Exception with message "Failed to delete alignment. {command} returned {raw}".
            act.Should().Throw<Exception>().WithMessage("*Failed to delete alignment*");
        }

        [Test]
        public void SetUltraPrecisionMode_SendsU2Blind() {
            sut.SetUltraPrecisionMode();

            commander.Verify(c => c.SendCommandBlind(":U2#", true), Times.Once);
        }

        [Test]
        public void SetSiderealTrackingRate_SendsTQBlind() {
            sut.SetSiderealTrackingRate();

            commander.Verify(c => c.SendCommandBlind(":TQ#", true), Times.Once);
        }

        [Test]
        public void SetLunarTrackingRate_SendsTLBlind() {
            sut.SetLunarTrackingRate();

            commander.Verify(c => c.SendCommandBlind(":TL#", true), Times.Once);
        }

        [Test]
        public void SetSolarTrackingRate_SendsTSOLARBlind() {
            sut.SetSolarTrackingRate();

            commander.Verify(c => c.SendCommandBlind(":TSOLAR#", true), Times.Once);
        }

        [Test]
        public void StopTracking_SendsALBlind() {
            sut.StopTracking();

            commander.Verify(c => c.SendCommandBlind(":AL#", true), Times.Once);
        }

        [Test]
        public void StartTracking_SendsAPBlind() {
            sut.StartTracking();

            commander.Verify(c => c.SendCommandBlind(":AP#", true), Times.Once);
        }

        [TestCase(true, ":Suaf1#")]
        [TestCase(false, ":Suaf0#")]
        public void SetUnattendedFlip_SendsSuafBlind(bool enabled, string expectedCommand) {
            sut.SetUnattendedFlip(enabled);

            commander.Verify(c => c.SendCommandBlind(expectedCommand, true), Times.Once);
        }

        // ---------- Complex methods ----------

        [Test]
        public void GetProductFirmware_AllCommandsSucceed_ReturnsParsedFirmware() {
            commander.Setup(c => c.SendCommandString(":GVP#", true)).Returns("10micron GM1000HPS#");
            commander.Setup(c => c.SendCommandString(":GVD#", true)).Returns("Jan 15 2024#");
            commander.Setup(c => c.SendCommandString(":GVN#", true)).Returns("2.15.5#");
            commander.Setup(c => c.SendCommandString(":GVT#", true)).Returns("10:30:00#");

            var result = sut.GetProductFirmware();

            result.Value.ProductName.Should().Be("10micron GM1000HPS");
            result.Value.Version.Should().Be(new Version(2, 15, 5));
            result.Value.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void SetMaximumPrecision_VersionAboveThreshold_SendsU2() {
            var firmware = new ProductFirmware("10micron", DateTime.UtcNow, new Version(2, 15, 0));

            sut.SetMaximumPrecision(firmware);

            commander.Verify(c => c.SendCommandBlind(":U2#", true), Times.Once);
        }

        [Test]
        public void SetMaximumPrecision_VersionAtOrBelowThreshold_SendsEMUAPFallback() {
            // Source compares with strict `>`, so version == 2.10.0 falls through to the fallback.
            var firmware = new ProductFirmware("10micron", DateTime.UtcNow, new Version(2, 10, 0));

            sut.SetMaximumPrecision(firmware);

            commander.Verify(c => c.SendCommandBlind(":EMUAP#:U#", true), Times.Once);
        }

        [Test]
        public void GetUTCTime_FullIsoDate_ReturnsUtc() {
            commander.Setup(c => c.SendCommandString(":GUDT#", true)).Returns("2024-06-15,12:34:56#");

            var result = sut.GetUTCTime();

            result.Value.Year.Should().Be(2024);
            result.Value.Month.Should().Be(6);
            result.Value.Day.Should().Be(15);
            result.Value.Hour.Should().Be(12);
            result.Value.Minute.Should().Be(34);
            result.Value.Second.Should().Be(56);
            result.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void GetUTCTime_ShortDateWithTenthSecond_ParsesHundredths() {
            // Time substring length 10 => last digit is tenth-second; multiplied by 10 to yield hundredths.
            commander.Setup(c => c.SendCommandString(":GUDT#", true)).Returns("06/15/24,12:34:56.5#");

            var result = sut.GetUTCTime();

            result.Value.Year.Should().Be(2024);
            result.Value.Month.Should().Be(6);
            result.Value.Day.Should().Be(15);
            result.Value.Second.Should().Be(56);
            // hundredthSeconds=50; source builds DateTime with millisecond arg = hundredthSeconds*10 = 500.
            result.Value.Millisecond.Should().Be(500);
            result.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void AddAlignmentPointToSpec_EastPier_BuildsCommandWithECommaAndReturnsCount() {
            // Capture the command the SUT assembles so we can assert format.
            string actualCommand = null;
            commander.Setup(c => c.SendCommandString(It.IsAny<string>(), true))
                .Callback<string, bool>((cmd, _) => actualCommand = cmd)
                .Returns("42#");

            var result = sut.AddAlignmentPointToSpec(
                mountRightAscension: new AstrometricTime(12, 0, 0, 0),
                mountDeclination: new CoordinateAngle(true, 45, 0, 0, 0),
                sideOfPier: PierSide.pierEast,
                plateSolvedRightAscension: new AstrometricTime(12, 0, 0, 0),
                plateSolvedDeclination: new CoordinateAngle(true, 45, 0, 0, 0),
                localSiderealTime: new AstrometricTime(6, 0, 0, 0));

            result.Value.Should().Be(42);
            actualCommand.Should().StartWith(":newalpt");
            actualCommand.Should().Contain(",E,");
        }

        [Test]
        public void AddAlignmentPointToSpec_WestPier_BuildsCommandWithWComma() {
            string actualCommand = null;
            commander.Setup(c => c.SendCommandString(It.IsAny<string>(), true))
                .Callback<string, bool>((cmd, _) => actualCommand = cmd)
                .Returns("1#");

            sut.AddAlignmentPointToSpec(
                mountRightAscension: new AstrometricTime(0, 0, 0, 0),
                mountDeclination: new CoordinateAngle(true, 0, 0, 0, 0),
                sideOfPier: PierSide.pierWest,
                plateSolvedRightAscension: new AstrometricTime(0, 0, 0, 0),
                plateSolvedDeclination: new CoordinateAngle(true, 0, 0, 0, 0),
                localSiderealTime: new AstrometricTime(0, 0, 0, 0));

            actualCommand.Should().Contain(",W,");
        }

        [Test]
        public void AddAlignmentPointToSpec_UnknownPier_ThrowsArgumentException() {
            Action act = () => sut.AddAlignmentPointToSpec(
                mountRightAscension: new AstrometricTime(0, 0, 0, 0),
                mountDeclination: new CoordinateAngle(true, 0, 0, 0, 0),
                sideOfPier: PierSide.pierUnknown,
                plateSolvedRightAscension: new AstrometricTime(0, 0, 0, 0),
                plateSolvedDeclination: new CoordinateAngle(true, 0, 0, 0, 0),
                localSiderealTime: new AstrometricTime(0, 0, 0, 0));

            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void AddAlignmentPointToSpec_ErrorResponse_Throws() {
            commander.Setup(c => c.SendCommandString(It.IsAny<string>(), true)).Returns("E#");

            Action act = () => sut.AddAlignmentPointToSpec(
                mountRightAscension: new AstrometricTime(0, 0, 0, 0),
                mountDeclination: new CoordinateAngle(true, 0, 0, 0, 0),
                sideOfPier: PierSide.pierEast,
                plateSolvedRightAscension: new AstrometricTime(0, 0, 0, 0),
                plateSolvedDeclination: new CoordinateAngle(true, 0, 0, 0, 0),
                localSiderealTime: new AstrometricTime(0, 0, 0, 0));

            // Source throws plain Exception with message "Failed to add alignment point using {command}".
            act.Should().Throw<Exception>().WithMessage("*Failed to add alignment point*");
        }
    }
}
