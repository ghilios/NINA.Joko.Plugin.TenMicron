using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.ModelManagement;
using NINA.Joko.Plugin.TenMicron.Tests.TestHelpers;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.ModelManagement {

    [TestFixture]
    public class ModelPointGeneratorTests {

        // Static sea-level horizon used across point-generation tests.
        private static readonly CustomHorizon SeaHorizon = BuildConstantHorizon(0.0);

        private static CustomHorizon BuildConstantHorizon(double altitudeDegrees) {
            // NINA's CustomHorizon parses a simple two-column "az alt" text format.
            var definition = $"0 {altitudeDegrees}\n360 {altitudeDegrees}";
            using var reader = new StringReader(definition);
            return CustomHorizon.FromReader_Standard(reader);
        }

        private static ModelPointGenerator BuildSut(
            Mock<ITenMicronOptions> options = null,
            Mock<IMountMediator> mountMediator = null,
            double latitudeDeg = 40.0,
            double longitudeDeg = -74.0) {
            options ??= MockOptionsBuilder.Build();
            mountMediator ??= new Mock<IMountMediator>();
            mountMediator.Setup(m => m.GetInfo()).Returns(new MountInfo { MeridianLimitDegrees = 10 });

            var profile = MockProfileBuilder.Build(latitudeDeg, longitudeDeg);
            var telescope = new Mock<ITelescopeMediator>();
            var weather = new Mock<IWeatherDataMediator>();
            weather.Setup(w => w.GetInfo()).Returns(new WeatherDataInfo { Connected = false });

            return new ModelPointGenerator(profile.Object, telescope.Object, weather.Object, options.Object, mountMediator.Object);
        }

        [Test]
        public void GenerateGoldenSpiral_NumPointsAboveMax_Throws() {
            var sut = BuildSut();

            Action act = () => sut.GenerateGoldenSpiral(numPoints: ModelPointGenerator.MAX_POINTS + 1, SeaHorizon);

            act.Should().Throw<Exception>().WithMessage("*do not support more than*");
        }

        [Test]
        public void GenerateGoldenSpiral_NumPointsBelow3_Throws() {
            var sut = BuildSut();

            Action act = () => sut.GenerateGoldenSpiral(numPoints: 2, SeaHorizon);

            act.Should().Throw<Exception>().WithMessage("*At least 3 points*");
        }

        [Test]
        public void GenerateGoldenSpiral_HappyPath_ReturnsAtLeastRequestedValidPoints() {
            NinaAssetGate.RequireNina();
            // Wide bounds so most generated points should be valid.
            var options = MockOptionsBuilder.Build(minAltitude: 1, maxAltitude: 89);
            var sut = BuildSut(options);

            var points = sut.GenerateGoldenSpiral(numPoints: 10, SeaHorizon);

            points.Count(p => p.ModelPointState == ModelPointStateEnum.Generated).Should().BeGreaterOrEqualTo(10);
        }

        [Test]
        public void GenerateGoldenSpiral_AltitudeClamped_To_0p1_And_89p9() {
            NinaAssetGate.RequireNina();
            var sut = BuildSut();

            var points = sut.GenerateGoldenSpiral(numPoints: 10, SeaHorizon);

            points.Should().OnlyContain(p => p.Altitude >= 0.1d && p.Altitude <= 89.9d);
        }

        [Test]
        public void GenerateGoldenSpiral_StandardAzimuthBounds_MarksOutsidePoints() {
            NinaAssetGate.RequireNina();
            // Standard bounds (Min < Max): only [120, 240] valid.
            var options = MockOptionsBuilder.Build(minAzimuth: 120, maxAzimuth: 240);
            var sut = BuildSut(options);

            var points = sut.GenerateGoldenSpiral(numPoints: 30, SeaHorizon);

            foreach (var p in points) {
                if (p.Azimuth < 120 || p.Azimuth >= 240) {
                    // Points outside az band should be marked OutsideAzimuthBounds, unless they were
                    // rejected by a higher-priority filter (altitude bounds, meridian).
                    p.ModelPointState.Should().NotBe(ModelPointStateEnum.Generated,
                        $"point at az={p.Azimuth}, alt={p.Altitude} is outside [120, 240) but was marked Generated");
                }
            }
        }

        [Test]
        public void GenerateGoldenSpiral_WrappedAzimuthBounds_SouthernHemisphereCase() {
            NinaAssetGate.RequireNina();
            // Wrapped bounds (Min > Max): only [0, 90] and [270, 360) valid. Regression for commit 3e33b20.
            var options = MockOptionsBuilder.Build(minAzimuth: 270, maxAzimuth: 90);
            var sut = BuildSut(options, latitudeDeg: -33.0);

            var points = sut.GenerateGoldenSpiral(numPoints: 30, SeaHorizon);

            foreach (var p in points) {
                bool insideWrap = p.Azimuth >= 270 || p.Azimuth <= 90;
                if (!insideWrap) {
                    p.ModelPointState.Should().NotBe(ModelPointStateEnum.Generated,
                        $"point at az={p.Azimuth} is in the rejected wrap-band (90, 270) but was marked Generated");
                }
            }
        }

        [Test]
        public void GenerateGoldenSpiral_AltitudeBounds_MarkOutsideAltitudeBounds() {
            NinaAssetGate.RequireNina();
            var options = MockOptionsBuilder.Build(minAltitude: 40, maxAltitude: 60);
            var sut = BuildSut(options);

            var points = sut.GenerateGoldenSpiral(numPoints: 10, SeaHorizon);

            foreach (var p in points) {
                if (p.Altitude < 40 || p.Altitude > 60) {
                    p.ModelPointState.Should().Be(ModelPointStateEnum.OutsideAltitudeBounds);
                }
            }
        }

        [Test, CancelAfter(10000)]
        public void GenerateGoldenSpiral_HighHorizon_TerminatesAndReturns() {
            NinaAssetGate.RequireNina();
            // Horizon at 89.5° — almost nothing should validate. The convergence loop must still
            // terminate (guard against infinite retry).
            var horizon = BuildConstantHorizon(89.5);
            var sut = BuildSut();

            var points = sut.GenerateGoldenSpiral(numPoints: 5, horizon);

            points.Should().NotBeNull();
        }

        [Test]
        public void GenerateSiderealPath_EndBeforeStart_Throws() {
            var sut = BuildSut();
            var now = DateTime.UtcNow;
            var coords = new Coordinates(ra: Angle.ByHours(6), dec: Angle.ByDegree(30), epoch: Epoch.JNOW, dateTime: new NINA.Joko.Plugin.TenMicron.Utility.ConstantDateTime(now));

            Action act = () => sut.GenerateSiderealPath(coords, Angle.ByDegree(1), startTime: now, endTime: now - TimeSpan.FromHours(1), SeaHorizon);

            act.Should().Throw<Exception>().WithMessage("*End time*comes before start time*");
        }

        [Test]
        public void GenerateSiderealPath_RangeOver1Day_Throws() {
            var sut = BuildSut();
            var now = DateTime.UtcNow;
            var coords = new Coordinates(ra: Angle.ByHours(6), dec: Angle.ByDegree(30), epoch: Epoch.JNOW, dateTime: new NINA.Joko.Plugin.TenMicron.Utility.ConstantDateTime(now));

            Action act = () => sut.GenerateSiderealPath(coords, Angle.ByDegree(1), startTime: now, endTime: now + TimeSpan.FromDays(2), SeaHorizon);

            act.Should().Throw<Exception>().WithMessage("*more than 1 day beyond*");
        }

        [Test]
        public void GenerateSiderealPath_RaDeltaBelow1Arcsec_Throws() {
            var sut = BuildSut();
            var now = DateTime.UtcNow;
            var coords = new Coordinates(ra: Angle.ByHours(6), dec: Angle.ByDegree(30), epoch: Epoch.JNOW, dateTime: new NINA.Joko.Plugin.TenMicron.Utility.ConstantDateTime(now));

            // 0.0001° = 0.36 arcsec → less than 1 arcsec; converted via Angle.ByHours, raDelta.Hours
            // corresponds to roughly 1.4ms — well under 1s.
            Action act = () => sut.GenerateSiderealPath(coords, Angle.ByDegree(0.0001), startTime: now, endTime: now + TimeSpan.FromHours(1), SeaHorizon);

            act.Should().Throw<Exception>().WithMessage("*cannot be less than 1 arc second*");
        }

        [Test]
        public void ToEquatorial_KnownAltAz_RoundTripsToSameTopocentric() {
            NinaAssetGate.RequireNina();
            // Regression: ModelPointGenerator.ToEquatorial previously swapped the `azimuth:` and
            // `altitude:` named arguments when constructing TopocentricCoordinates, so a known
            // (alt, az) input came back as (az, alt) after the round trip. Asserts the identity now.
            var sut = BuildSut(latitudeDeg: 40, longitudeDeg: -74);
            var now = new DateTime(2025, 6, 21, 6, 0, 0, DateTimeKind.Utc);
            const double inputAlt = 30.0;
            const double inputAz = 120.0;

            var coords = sut.ToEquatorial(inputAlt, inputAz, now);

            var coordsAtTime = new Coordinates(
                ra: Angle.ByHours(coords.RA),
                dec: Angle.ByDegree(coords.Dec),
                epoch: Epoch.JNOW,
                dateTime: new NINA.Joko.Plugin.TenMicron.Utility.ConstantDateTime(now));
            var topo = coordsAtTime.Transform(
                latitude: Angle.ByDegree(40),
                longitude: Angle.ByDegree(-74),
                elevation: 100);
            topo.Altitude.Degree.Should().BeApproximately(inputAlt, 0.5);
            topo.Azimuth.Degree.Should().BeApproximately(inputAz, 0.5);
        }
    }
}
