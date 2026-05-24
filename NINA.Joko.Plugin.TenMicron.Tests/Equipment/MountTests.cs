using FluentAssertions;
using Moq;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NUnit.Framework;

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
            // Only asserts hours/minutes — seconds/hundredths are entangled with the grammar
            // ambiguity FLAG covered in MountResponseParserTests. This test verifies the *wiring*:
            // the right command goes out, and the response is parsed (not that parsed values are
            // semantically correct).
            commander.Setup(c => c.SendCommandString(":GR#", true)).Returns("12:34:56.78#");

            var result = sut.GetRightAscension();

            result.Value.Hours.Should().Be(12);
            result.Value.Minutes.Should().Be(34);
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
    }
}
