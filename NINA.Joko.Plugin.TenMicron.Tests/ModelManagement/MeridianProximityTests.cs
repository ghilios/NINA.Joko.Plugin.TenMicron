using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.ModelManagement;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.TenMicron.Tests.ModelManagement {

    [TestFixture]
    public class MeridianProximityTests {
        // Reference values computed independently from the standard horizontal -> hour angle
        // spherical trigonometry: HA = atan2(-sin(az) cos(alt), sin(alt) cos(lat) - cos(alt) sin(lat) cos(az))

        [Test]
        public void GetMinutesFromMeridian_DueSouth_IsOnMeridian() {
            ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 30.0, azimuthDegrees: 180.0, latitudeDegrees: 40.0)
                .Should().BeApproximately(0.0, 0.01);
        }

        [Test]
        public void GetMinutesFromMeridian_DueNorthAbovePole_IsOnMeridian() {
            // A point between the pole and the zenith transits the upper meridian at azimuth 0
            ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 50.0, azimuthDegrees: 0.0, latitudeDegrees: 40.0)
                .Should().BeApproximately(0.0, 0.01);
        }

        [Test]
        public void GetMinutesFromMeridian_DueNorthBelowPole_IsAtLowerMeridian() {
            var minutes = ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 20.0, azimuthDegrees: 0.0, latitudeDegrees: 40.0);
            Math.Abs(minutes).Should().BeApproximately(720.0, 0.01);
        }

        [Test]
        public void GetMinutesFromMeridian_DueEast_IsHoursEastOfMeridian() {
            ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 45.0, azimuthDegrees: 90.0, latitudeDegrees: 40.0)
                .Should().BeApproximately(-210.185122, 0.01);
        }

        [Test]
        public void GetMinutesFromMeridian_DueWest_MirrorsDueEast() {
            ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 45.0, azimuthDegrees: 270.0, latitudeDegrees: 40.0)
                .Should().BeApproximately(210.185122, 0.01);
        }

        [Test]
        public void GetMinutesFromMeridian_SouthernHemisphereDueNorth_IsOnMeridian() {
            ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 30.0, azimuthDegrees: 0.0, latitudeDegrees: -35.0)
                .Should().BeApproximately(0.0, 0.01);
        }

        [Test]
        public void GetMinutesFromMeridian_ReportedBlackDot_IsNowhereNearMeridian() {
            // The point from the user report: alt 49, az 97 was flagged "Too Close to Meridian"
            // by the old RA-vs-wall-clock comparison. It is actually ~184 minutes east of it.
            var minutes = ModelPointGenerator.GetMinutesFromMeridian(altitudeDegrees: 49.0, azimuthDegrees: 97.0, latitudeDegrees: 40.0);
            Math.Abs(minutes).Should().BeGreaterThan(60.0);
        }
    }
}
