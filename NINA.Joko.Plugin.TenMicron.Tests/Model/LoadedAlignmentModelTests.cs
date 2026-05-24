using System.Collections.Immutable;
using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Model {

    [TestFixture]
    public class LoadedAlignmentModelTests {

        private const string NinaNativeReason = "Requires NINA install (JPL ephemeris + migration DB). Move tests next to NINA.exe to run.";

        [Test]
        public void SynchronizePoints_NoStars_LeavesEmptyCollection() {
            var sut = new LoadedAlignmentModel {
                Latitude = Angle.ByDegree(40),
                Longitude = Angle.ByDegree(-74),
                SiteElevation = 100,
                OriginalAlignmentStars = ImmutableList.Create<AlignmentStarInfo>(),
            };

            sut.SynchronizePoints();

            sut.AlignmentStars.Should().BeEmpty();
            sut.MaxRMSError.Should().Be(0.0);
        }

        [Test, Ignore(NinaNativeReason)]
        public void SynchronizePoints_OneStar_PopulatesAlignmentStars() {
            var star = new AlignmentStarInfo(
                new AstrometricTime(1, 0, 0, 0),
                new CoordinateAngle(true, 30, 0, 0, 0),
                errorArcseconds: 7m);
            var sut = new LoadedAlignmentModel {
                Latitude = Angle.ByDegree(40),
                Longitude = Angle.ByDegree(-74),
                SiteElevation = 100,
                OriginalAlignmentStars = ImmutableList.Create(star),
            };

            sut.SynchronizePoints();

            sut.AlignmentStars.Should().HaveCount(1);
            sut.AlignmentStars[0].ErrorArcsec.Should().Be(7.0);
        }

        [Test]
        public void MaxRMSError_TracksMaxAcrossStars() {
            var s1 = new AlignmentStarInfo(new AstrometricTime(0, 0, 0, 0), new CoordinateAngle(true, 0, 0, 0, 0), 3m);
            var s2 = new AlignmentStarInfo(new AstrometricTime(0, 0, 0, 0), new CoordinateAngle(true, 0, 0, 0, 0), 9m);
            var s3 = new AlignmentStarInfo(new AstrometricTime(0, 0, 0, 0), new CoordinateAngle(true, 0, 0, 0, 0), 1m);

            var sut = new LoadedAlignmentModel {
                OriginalAlignmentStars = ImmutableList.Create(s1, s2, s3),
            };

            sut.MaxRMSError.Should().Be(9.0);
        }

        [Test]
        public void Clear_ResetsAllFields() {
            var sut = new LoadedAlignmentModel {
                ModelName = "x",
                RightAscensionAzimuth = 1m,
                ModelTerms = 5,
                AlignmentStarCount = 3,
                OriginalAlignmentStars = ImmutableList.Create(
                    new AlignmentStarInfo(new AstrometricTime(0, 0, 0, 0), new CoordinateAngle(true, 0, 0, 0, 0), 1m)),
            };

            sut.Clear();

            sut.ModelName.Should().Be("");
            sut.ModelTerms.Should().Be(-1);
            sut.AlignmentStarCount.Should().Be(-1);
            sut.OriginalAlignmentStars.Should().BeEmpty();
            sut.AlignmentStars.Should().BeEmpty();
        }
    }
}
