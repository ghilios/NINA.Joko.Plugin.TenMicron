using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.TestHelpers {

    // Tests that exercise NINA.Astrometry coordinate transforms need NINA's native libs
    // (NOVAS31lib, SOFAlib), the JPL ephemeris, and the migration SQL — none of which ship in
    // the NuGet package. The NativeLibraryFixture copies them from a local NINA install at
    // session start if it can find one. Tests that depend on those assets call RequireNina()
    // in their [SetUp] so they self-skip on CI / dev machines without NINA installed.
    public static class NinaAssetGate {

        public static void RequireNina() {
            if (!NativeLibraryFixture.NinaAssetsAvailable) {
                Assert.Ignore(
                    "Skipped: NINA install not detected at the default path or NINA_INSTALL_PATH. " +
                    "Install NINA, or set the NINA_INSTALL_PATH env var to its install directory, to run this test.");
            }
        }
    }
}
