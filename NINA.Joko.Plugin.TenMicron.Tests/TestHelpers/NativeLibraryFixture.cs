using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

// SetUpFixture must live in the root test namespace so NUnit applies it to every test in the
// assembly (it applies to tests under its namespace and below).
namespace NINA.Joko.Plugin.TenMicron.Tests {

    // NINA.Astrometry P/Invokes a native NOVAS DLL using the name "NOVAS31lib.dll". Outside a
    // NINA host process the OS does not find it under that name. This fixture supplies the
    // natives via two paths:
    //
    //   1. Build-time: the test csproj's CopyNinaNativeAssets target seeds External/x64/NOVAS,
    //      External/x64/SOFA, and External/JPLEPH from the ASCOM.Tools 1.0.112 NuGet package,
    //      which ships the libnovas/libsofa binaries and JPL ephemeris. This makes CI runs
    //      self-sufficient without a NINA install.
    //   2. Run-time (optional): if a real NINA install exists at the default path (or
    //      NINA_INSTALL_PATH), we overlay its External/ tree on top, picking up any newer/
    //      JPL-updated binaries it ships.
    //
    // The fixture also registers a DllImportResolver against NINA.Astrometry so the natives
    // load from either the External/x64 location or the NuGet runtimes/ fallback.
    //
    // Tests that need a working coordinate transform call NinaAssetGate.RequireNina() in their
    // [SetUp]; that gate auto-Ignores them only if the natives are missing from the bin.
    [SetUpFixture]
    public class NativeLibraryFixture {

        private const string DefaultNinaInstallPath = @"C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy";

        public static bool NinaAssetsAvailable { get; private set; }
        public static bool NinaDatabaseAvailable { get; private set; }

        [OneTimeSetUp]
        public void Setup() {
            TryOverlayNinaInstallAssets();
            NinaAssetsAvailable = File.Exists(Path.Combine(AppContext.BaseDirectory, "External", "x64", "NOVAS", "NOVAS31lib.dll"));
            NinaDatabaseAvailable = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Database", "Migration"));
            if (!NinaAssetsAvailable) {
                TestContext.Progress.WriteLine("NOVAS31lib.dll not found in test bin. NINA-dependent tests will be skipped.");
            }
            RegisterDllResolvers();
        }

        // Optional: if a real NINA install is present, copy its External/ and Database/ over the
        // build-time seed. External/ gets newer JPL ephemeris updates; Database/ is the only way
        // to satisfy tests that touch UT1/UTC lookups, since NINA's migration SQL doesn't ship
        // in any NuGet package. Silent no-op when no install is found.
        private static void TryOverlayNinaInstallAssets() {
            var ninaPath = Environment.GetEnvironmentVariable("NINA_INSTALL_PATH") ?? DefaultNinaInstallPath;
            var externalSrc = Path.Combine(ninaPath, "External");
            var databaseSrc = Path.Combine(ninaPath, "Database");
            try {
                if (Directory.Exists(externalSrc)) {
                    CopyDirectoryRecursive(externalSrc, Path.Combine(AppContext.BaseDirectory, "External"));
                }
                if (Directory.Exists(databaseSrc)) {
                    CopyDirectoryRecursive(databaseSrc, Path.Combine(AppContext.BaseDirectory, "Database"));
                }
            } catch (Exception ex) {
                TestContext.Progress.WriteLine($"Failed to overlay NINA assets from '{ninaPath}': {ex.Message}");
            }
        }

        // Mirror-copy that skips files already present with the same or newer timestamp. Locked
        // files (e.g. a NOVAS DLL already loaded by a previous run) are tolerated since the
        // existing copy is what we want.
        private static void CopyDirectoryRecursive(string source, string dest) {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source)) {
                var destFile = Path.Combine(dest, Path.GetFileName(file));
                var srcInfo = new FileInfo(file);
                var destInfo = new FileInfo(destFile);
                if (destInfo.Exists && destInfo.LastWriteTimeUtc >= srcInfo.LastWriteTimeUtc && destInfo.Length == srcInfo.Length) {
                    continue;
                }
                try {
                    File.Copy(file, destFile, overwrite: true);
                } catch (IOException) {
                    // File is locked — fine, the existing copy will serve.
                }
            }
            foreach (var subdir in Directory.GetDirectories(source)) {
                CopyDirectoryRecursive(subdir, Path.Combine(dest, Path.GetFileName(subdir)));
            }
        }

        private static void RegisterDllResolvers() {
            var assemblies = new[] {
                typeof(NINA.Astrometry.AstroUtil).Assembly,
            };

            foreach (var asm in assemblies) {
                try {
                    NativeLibrary.SetDllImportResolver(asm, Resolve);
                } catch (InvalidOperationException) {
                    // Already registered (test runner reuse) — fine.
                }
            }
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
            string ninaDllName = null;
            string nugetDllName = null;
            string nugetSubFolder = null;
            if (libraryName.Equals("NOVAS31lib", StringComparison.OrdinalIgnoreCase) ||
                libraryName.Equals("NOVAS31lib.dll", StringComparison.OrdinalIgnoreCase)) {
                ninaDllName = "NOVAS31lib.dll";
                nugetDllName = "libnovas.dll";
                nugetSubFolder = "Novas31/NOVASLibraries";
            } else if (libraryName.Equals("SOFAlib", StringComparison.OrdinalIgnoreCase) ||
                       libraryName.Equals("SOFAlib.dll", StringComparison.OrdinalIgnoreCase)) {
                ninaDllName = "SOFAlib.dll";
                nugetDllName = "libsofa.dll";
                nugetSubFolder = "SOFALibraries";
            }

            if (ninaDllName == null) {
                return IntPtr.Zero;
            }

            var arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "x86";
            var rid = "win-" + arch;
            // NINA's own DllLoader looks at External\{arch}\NOVAS\{ninaDllName} (and SOFA/...).
            var subFolderInNina = ninaDllName.Contains("NOVAS") ? "NOVAS" : "SOFA";
            var candidates = new[] {
                // Preferred: the NINA-shipped DLL we copied to External\x64\... at session start.
                Path.Combine(AppContext.BaseDirectory, "External", arch, subFolderInNina, ninaDllName),
                // Fallback: the NuGet-packed lib (different binary, but compatible API for what tests exercise).
                Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", nugetSubFolder, rid, nugetDllName),
                Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", nugetDllName),
                Path.Combine(AppContext.BaseDirectory, nugetDllName),
            };

            foreach (var path in candidates) {
                if (File.Exists(path)) {
                    return NativeLibrary.Load(path);
                }
            }
            return IntPtr.Zero;
        }
    }
}
