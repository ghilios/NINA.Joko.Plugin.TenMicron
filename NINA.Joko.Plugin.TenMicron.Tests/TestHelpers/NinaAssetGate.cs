using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.TestHelpers {

    // Tests that exercise NINA.Astrometry split into two cohorts:
    //   - Most need only the native NOVAS/SOFA libs plus the JPL ephemeris. These are seeded
    //     at build time from the ASCOM.Tools 1.0.112 NuGet harvest, so they run on CI.
    //   - A few transit DatabaseInteraction.GetUT1_UTC, which reads NINA's Database/Migration
    //     SQL files. Those only ship in a real NINA install and self-skip on CI.
    public static class NinaAssetGate {

        public static void RequireNina() {
            if (!NativeLibraryFixture.NinaAssetsAvailable) {
                Assert.Ignore(
                    "Skipped: NOVAS31lib.dll not found in the test bin. " +
                    "The ASCOM.Tools NuGet harvest should normally provide it - this points to a build misconfiguration.");
            }
        }

        public static void RequireNinaDatabase() {
            RequireNina();
            if (!NativeLibraryFixture.NinaDatabaseAvailable) {
                Assert.Ignore(
                    "Skipped: NINA Database/Migration folder not found. " +
                    "This test transits NINA's DatabaseInteraction.GetUT1_UTC, which requires a local NINA install " +
                    "(set NINA_INSTALL_PATH or install at the default location).");
            }
        }
    }
}
