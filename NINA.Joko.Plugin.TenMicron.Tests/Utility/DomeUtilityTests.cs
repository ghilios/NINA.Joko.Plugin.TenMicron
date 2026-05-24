using System;
using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.TenMicron.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Utility {

    [TestFixture]
    public class DomeUtilityTests {

        [Test]
        public void CalculateDomeAzimuthRange_KnownGeometry_ProducesExpectedRange() {
            // alt=0, az=180, radius=2000mm, shutter=1000mm
            // radiusAtAltitude = cos(0) * 2000 = 2000
            // oppositeOverHypotenuse = 500 / 2000 = 0.25
            // apertureThresholdRadians = asin(0.25) ≈ 0.25268 rad ≈ 14.4775°
            var (left, right) = DomeUtility.CalculateDomeAzimuthRange(
                altitudeAngle: Angle.ByDegree(0),
                azimuthAngle: Angle.ByDegree(180),
                domeRadius: 2000,
                domeShutterWidthMm: 1000);

            var expected = Math.Asin(0.25) * 180.0 / Math.PI;
            left.Degree.Should().BeApproximately(180.0 - expected, 1e-6);
            right.Degree.Should().BeApproximately(180.0 + expected, 1e-6);
        }

        [Test]
        public void CalculateDomeAzimuthRange_ShutterTooWideForRadius_FallsBackTo45Degrees() {
            // oppositeOverHypotenuse > 1 → asin → NaN → fallback 45°
            var (left, right) = DomeUtility.CalculateDomeAzimuthRange(
                altitudeAngle: Angle.ByDegree(0),
                azimuthAngle: Angle.ByDegree(90),
                domeRadius: 1000,
                domeShutterWidthMm: 3000);

            left.Degree.Should().BeApproximately(45.0, 1e-9);
            right.Degree.Should().BeApproximately(135.0, 1e-9);
        }

        [Test]
        public void CalculateDomeAzimuthRange_CapsAtPiOver4_WhenAsinExceedsIt() {
            // oppositeOverHypotenuse = 750 / 1000 = 0.75 → asin ≈ 0.8481 rad ≈ 48.59° > 45°
            // Code caps at PI/4 = 45°
            var (left, right) = DomeUtility.CalculateDomeAzimuthRange(
                altitudeAngle: Angle.ByDegree(0),
                azimuthAngle: Angle.ByDegree(90),
                domeRadius: 1000,
                domeShutterWidthMm: 1500);

            left.Degree.Should().BeApproximately(45.0, 1e-9);
            right.Degree.Should().BeApproximately(135.0, 1e-9);
        }

        [Test]
        public void CalculateDomeAzimuthRange_NearZenith_FallsBack_DueToShrinkingRadius() {
            // At alt=89°, radiusAtAltitude = cos(89°)*1000 ≈ 17.45mm; with shutter=1000mm,
            // opposite/hypotenuse ≫ 1 → asin NaN → 45° fallback.
            var (left, right) = DomeUtility.CalculateDomeAzimuthRange(
                altitudeAngle: Angle.ByDegree(89),
                azimuthAngle: Angle.ByDegree(0),
                domeRadius: 1000,
                domeShutterWidthMm: 1000);

            left.Degree.Should().BeApproximately(-45.0, 1e-9);
            right.Degree.Should().BeApproximately(45.0, 1e-9);
        }
    }
}
