using Moq;
using NINA.Joko.Plugin.TenMicron.Interfaces;

namespace NINA.Joko.Plugin.TenMicron.Tests.TestHelpers {

    public static class MockOptionsBuilder {

        public static Mock<ITenMicronOptions> Build(
            int minAltitude = 20,
            int maxAltitude = 85,
            double minAzimuth = 0.0,
            double maxAzimuth = 360.0,
            double decJitterSigmaDegrees = 0.0) {
            var opts = new Mock<ITenMicronOptions>();
            opts.SetupGet(x => x.MinPointAltitude).Returns(minAltitude);
            opts.SetupGet(x => x.MaxPointAltitude).Returns(maxAltitude);
            opts.SetupGet(x => x.MinPointAzimuth).Returns(minAzimuth);
            opts.SetupGet(x => x.MaxPointAzimuth).Returns(maxAzimuth);
            opts.SetupGet(x => x.DecJitterSigmaDegrees).Returns(decJitterSigmaDegrees);
            return opts;
        }
    }
}
