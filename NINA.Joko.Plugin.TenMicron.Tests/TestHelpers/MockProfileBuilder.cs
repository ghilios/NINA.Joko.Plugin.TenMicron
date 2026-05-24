using Moq;
using NINA.Profile.Interfaces;

namespace NINA.Joko.Plugin.TenMicron.Tests.TestHelpers {

    public static class MockProfileBuilder {

        public static Mock<IProfileService> Build(
            double latitudeDeg = 40.0,
            double longitudeDeg = -74.0,
            double elevationM = 100.0) {
            var astrometry = new Mock<IAstrometrySettings>();
            astrometry.SetupGet(x => x.Latitude).Returns(latitudeDeg);
            astrometry.SetupGet(x => x.Longitude).Returns(longitudeDeg);
            astrometry.SetupGet(x => x.Elevation).Returns(elevationM);
            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometry.Object);
            var svc = new Mock<IProfileService>();
            svc.SetupGet(x => x.ActiveProfile).Returns(profile.Object);
            return svc;
        }
    }
}
