using System;
using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Model {

    [TestFixture]
    public class AlignmentStarPointTests {

        // Every test in this fixture calls AlignmentStarPoint.FromAlignmentStarInfo, which transits
        // NINA's AstroUtil.GetLocalSiderealTime → SOFA/NOVAS native libs + NINA's JPL ephemeris file
        // (External\JPLEPH) + the NINA migration database (Database\Migration\). Those assets live in
        // a NINA install, not in the plugin's NuGet payload. Tests are kept here as living docs of
        // expected behavior and will run unchanged when the test DLL is dropped into a NINA bin folder.
        private const string NinaNativeReason = "Requires NINA install (JPL ephemeris + migration DB). Move tests next to NINA.exe to run.";


        // NOTE: AlignmentStarPoint.FromAlignmentStarInfo uses DateTime.Now internally for LST. Tests must
        // recompute LST in the test using the same site coords and a near-coincident timestamp, then
        // accept a small tolerance. Refactoring to inject an IDateTimeProvider would tighten this — flag.
        private const double TimeToleranceHours = 0.01; // ~36s

        [Test, Ignore(NinaNativeReason)]
        public void FromAlignmentStarInfo_ComputesRA_FromLstMinusLocalHour() {
            var localHour = new AstrometricTime(2, 0, 0, 0); // 2h hour-angle
            var dec = new CoordinateAngle(true, 45, 0, 0, 0);
            var starInfo = new AlignmentStarInfo(localHour, dec, errorArcseconds: 10m);
            var latitude = Angle.ByDegree(40.0);
            var longitude = Angle.ByDegree(-74.0);
            var now = DateTime.Now;

            var point = AlignmentStarPoint.FromAlignmentStarInfo(starInfo, modelMaxErrorArcsec: 10.0, latitude, longitude, siteElevation: 100);

            var expectedLst = AstroUtil.GetLocalSiderealTime(now, longitude.Degree);
            var expectedRaHours = AstroUtil.EuclidianModulus(expectedLst - localHour.ToAngle().Hours, 24);
            point.RightAscension.Hours.Should().BeApproximately(expectedRaHours, TimeToleranceHours);
        }

        [Test, Ignore(NinaNativeReason)]
        public void FromAlignmentStarInfo_AltitudeWithinPlausibleRange() {
            var starInfo = new AlignmentStarInfo(
                new AstrometricTime(0, 0, 0, 0),
                new CoordinateAngle(true, 30, 0, 0, 0),
                errorArcseconds: 5m);

            var point = AlignmentStarPoint.FromAlignmentStarInfo(
                starInfo,
                modelMaxErrorArcsec: 5.0,
                latitude: Angle.ByDegree(40),
                longitude: Angle.ByDegree(-74),
                siteElevation: 100);

            point.Altitude.Should().BeInRange(-90.0, 90.0);
            point.Azimuth.Should().BeInRange(0.0, 360.0);
            point.InvertedAltitude.Should().BeApproximately(90.0 - point.Altitude, 1e-9);
        }

        [Test, Ignore(NinaNativeReason)]
        public void ErrorPointRadius_AtMaxError_EqualsBoostedValue() {
            // When error == modelMaxErrorArcsec, errorRatio = 1 → radius = 5 * max(1, 1.5*1) = 7.5
            var starInfo = new AlignmentStarInfo(
                new AstrometricTime(0, 0, 0, 0),
                new CoordinateAngle(true, 0, 0, 0, 0),
                errorArcseconds: 10m);

            var point = AlignmentStarPoint.FromAlignmentStarInfo(
                starInfo,
                modelMaxErrorArcsec: 10.0,
                latitude: Angle.ByDegree(40),
                longitude: Angle.ByDegree(-74),
                siteElevation: 100);

            point.ErrorPointRadius.Should().BeApproximately(7.5, 1e-9);
        }

        [Test, Ignore(NinaNativeReason)]
        public void ErrorPointRadius_BelowThreshold_ClampsToFloor() {
            // errorRatio = 0.1 → 1.5*0.1=0.15 → max(1, 0.15)=1 → radius = 5*1 = 5
            var starInfo = new AlignmentStarInfo(
                new AstrometricTime(0, 0, 0, 0),
                new CoordinateAngle(true, 0, 0, 0, 0),
                errorArcseconds: 1m);

            var point = AlignmentStarPoint.FromAlignmentStarInfo(
                starInfo,
                modelMaxErrorArcsec: 10.0,
                latitude: Angle.ByDegree(40),
                longitude: Angle.ByDegree(-74),
                siteElevation: 100);

            point.ErrorPointRadius.Should().BeApproximately(5.0, 1e-9);
        }

        [Test, Ignore(NinaNativeReason)]
        public void ErrorArcsec_PropagatedFromInfo() {
            var starInfo = new AlignmentStarInfo(
                new AstrometricTime(0, 0, 0, 0),
                new CoordinateAngle(true, 0, 0, 0, 0),
                errorArcseconds: 12.34m);

            var point = AlignmentStarPoint.FromAlignmentStarInfo(
                starInfo,
                modelMaxErrorArcsec: 100.0,
                latitude: Angle.ByDegree(40),
                longitude: Angle.ByDegree(-74),
                siteElevation: 100);

            point.ErrorArcsec.Should().Be(12.34);
        }
    }
}
