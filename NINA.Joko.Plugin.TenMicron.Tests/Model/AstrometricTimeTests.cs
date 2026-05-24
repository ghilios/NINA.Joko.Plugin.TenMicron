using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Model {

    [TestFixture]
    public class AstrometricTimeTests {

        [Test]
        public void ToAngle_RoundTrip_KnownValue() {
            var sut = new AstrometricTime(hours: 12, minutes: 34, seconds: 56, hundredthSeconds: 78);

            // 12 + 34/60 + 56.78/3600 = 12.58243888... hours
            sut.ToAngle().Hours.Should().BeApproximately(12.582438889d, 1e-9);
        }

        [Test]
        public void FromAngle_Wraps_AtOrPast24Hours() {
            // 26 hours should wrap to 2 hours (Euclidian modulus by 24)
            var result = AstrometricTime.FromAngle(Angle.ByHours(26.0));

            result.Hours.Should().Be(2);
            result.Minutes.Should().Be(0);
            result.Seconds.Should().Be(0);
        }

        [Test]
        public void FromAngle_HundredthSeconds_Populated() {
            // FLAG: MountCommandResponses.cs:119 — `var hundredthSeconds = (int)(angleRemaining / 100.0d);`
            // The /100 always truncates hundredths to 0. CoordinateAngle.FromAngle at line 61 does it
            // correctly with `(int)angleRemaining`. Round-tripping 12h34m56.78s should preserve the .78.
            var original = new AstrometricTime(hours: 12, minutes: 34, seconds: 56, hundredthSeconds: 78);

            var roundTripped = AstrometricTime.FromAngle(original.ToAngle());

            roundTripped.Hours.Should().Be(12);
            roundTripped.Minutes.Should().Be(34);
            roundTripped.Seconds.Should().Be(56);
            roundTripped.HundredthSeconds.Should().Be(78);
        }

        [Test]
        public void RoundTenthSecond_BelowMidpoint_TruncatesDown() {
            var sut = new AstrometricTime(1, 2, 3, 14); // 14 hundredthSec → tenth = 1, remainder 4 < 5

            var rounded = sut.RoundTenthSecond();

            rounded.HundredthSeconds.Should().Be(10);
            rounded.Seconds.Should().Be(3);
        }

        [Test]
        public void RoundTenthSecond_AtOrAboveMidpoint_RoundsUp() {
            var sut = new AstrometricTime(1, 2, 3, 15); // remainder 5 → bumps to 2*10=20

            var rounded = sut.RoundTenthSecond();

            rounded.HundredthSeconds.Should().Be(20);
            rounded.Seconds.Should().Be(3);
        }

        [Test]
        public void RoundTenthSecond_TenthsRollover_BumpsSeconds() {
            var sut = new AstrometricTime(1, 2, 3, 95); // tenths=9, remainder=5 → bumps to 10 → seconds++

            var rounded = sut.RoundTenthSecond();

            rounded.HundredthSeconds.Should().Be(0);
            rounded.Seconds.Should().Be(4);
        }

        [Test]
        public void RoundTenthSecond_CascadingRollover_To_Hours() {
            var sut = new AstrometricTime(1, 59, 59, 95);

            var rounded = sut.RoundTenthSecond();

            rounded.Hours.Should().Be(2);
            rounded.Minutes.Should().Be(0);
            rounded.Seconds.Should().Be(0);
            rounded.HundredthSeconds.Should().Be(0);
        }
    }
}
