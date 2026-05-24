using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Model {

    [TestFixture]
    public class CoordinateAngleTests {

        [Test]
        public void ToAngle_PositiveDmsh_ReturnsExpectedDegree() {
            var sut = new CoordinateAngle(positive: true, degrees: 45, minutes: 30, seconds: 15, hundredthSeconds: 25);

            var result = sut.ToAngle();

            // 45 + 30/60 + 15.25/3600 = 45.50423611...
            result.Degree.Should().BeApproximately(45.504236111d, 1e-9);
        }

        [Test]
        public void ToAngle_NegativeDmsh_ReturnsNegativeDegree() {
            var sut = new CoordinateAngle(positive: false, degrees: 12, minutes: 0, seconds: 0, hundredthSeconds: 0);

            var result = sut.ToAngle();

            result.Degree.Should().BeApproximately(-12.0d, 1e-12);
        }

        [Test]
        public void ToAngle_Zero_ReturnsZero() {
            var sut = CoordinateAngle.ZERO;

            sut.ToAngle().Degree.Should().Be(0d);
        }

        [Test]
        public void FromAngle_RoundTrip_PositiveKnownValue() {
            var original = new CoordinateAngle(positive: true, degrees: 45, minutes: 30, seconds: 15, hundredthSeconds: 25);

            var roundTripped = CoordinateAngle.FromAngle(original.ToAngle());

            roundTripped.Positive.Should().BeTrue();
            roundTripped.Degrees.Should().Be(45);
            roundTripped.Minutes.Should().Be(30);
            roundTripped.Seconds.Should().Be(15);
            roundTripped.HundredthSeconds.Should().Be(25);
        }

        [Test]
        public void FromAngle_RoundTrip_NegativeKnownValue() {
            var original = new CoordinateAngle(positive: false, degrees: 23, minutes: 26, seconds: 13, hundredthSeconds: 50);

            var roundTripped = CoordinateAngle.FromAngle(original.ToAngle());

            roundTripped.Positive.Should().BeFalse();
            roundTripped.Degrees.Should().Be(23);
            roundTripped.Minutes.Should().Be(26);
            roundTripped.Seconds.Should().Be(13);
            roundTripped.HundredthSeconds.Should().Be(50);
        }

        [Test]
        public void FromAngle_Zero_ProducesZero() {
            var result = CoordinateAngle.FromAngle(NINA.Astrometry.Angle.ByDegree(0));

            result.Positive.Should().BeTrue();
            result.Degrees.Should().Be(0);
            result.Minutes.Should().Be(0);
            result.Seconds.Should().Be(0);
            result.HundredthSeconds.Should().Be(0);
        }

        [Test]
        public void RoundSeconds_BelowHalf_NoChange() {
            var sut = new CoordinateAngle(true, 10, 20, 30, 49);

            var rounded = sut.RoundSeconds();

            rounded.Degrees.Should().Be(10);
            rounded.Minutes.Should().Be(20);
            rounded.Seconds.Should().Be(30);
            rounded.HundredthSeconds.Should().Be(49);
        }

        [Test]
        public void RoundSeconds_AtOrAboveHalf_BumpsSeconds() {
            var sut = new CoordinateAngle(true, 10, 20, 30, 50);

            var rounded = sut.RoundSeconds();

            rounded.Degrees.Should().Be(10);
            rounded.Minutes.Should().Be(20);
            rounded.Seconds.Should().Be(31);
            rounded.HundredthSeconds.Should().Be(0);
        }

        [Test]
        public void RoundSeconds_SecondsRollover_BumpsMinutes() {
            var sut = new CoordinateAngle(true, 10, 20, 59, 99);

            var rounded = sut.RoundSeconds();

            rounded.Degrees.Should().Be(10);
            rounded.Minutes.Should().Be(21);
            rounded.Seconds.Should().Be(0);
            rounded.HundredthSeconds.Should().Be(0);
        }

        [Test]
        public void RoundSeconds_MinutesRollover_BumpsDegrees() {
            var sut = new CoordinateAngle(true, 10, 59, 59, 99);

            var rounded = sut.RoundSeconds();

            rounded.Degrees.Should().Be(11);
            rounded.Minutes.Should().Be(0);
            rounded.Seconds.Should().Be(0);
            rounded.HundredthSeconds.Should().Be(0);
        }
    }
}
