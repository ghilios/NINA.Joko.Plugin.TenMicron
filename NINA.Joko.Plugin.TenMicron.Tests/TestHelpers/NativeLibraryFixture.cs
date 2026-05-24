using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

// SetUpFixture must live in the root test namespace so NUnit applies it to every test in the
// assembly (it applies to tests under its namespace and below).
namespace NINA.Joko.Plugin.TenMicron.Tests {

    // NINA.Astrometry P/Invokes a native NOVAS DLL using the name "NOVAS31lib.dll". The NuGet
    // package ships it under runtimes/{rid}/native/Novas31/NOVASLibraries/{rid}/libnovas.dll, and
    // outside a NINA host process the OS does not find it under the expected name. This fixture
    // registers a DllImportResolver against the NINA.Astrometry assembly so tests can drive
    // coordinate transforms.
    [SetUpFixture]
    public class NativeLibraryFixture {

        [OneTimeSetUp]
        public void RegisterResolvers() {
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
            string nativeFileName = null;
            if (libraryName.Equals("NOVAS31lib", StringComparison.OrdinalIgnoreCase) ||
                libraryName.Equals("NOVAS31lib.dll", StringComparison.OrdinalIgnoreCase)) {
                nativeFileName = "libnovas.dll";
            } else if (libraryName.Equals("SOFAlib", StringComparison.OrdinalIgnoreCase) ||
                       libraryName.Equals("SOFAlib.dll", StringComparison.OrdinalIgnoreCase)) {
                nativeFileName = "libsofa.dll";
            }

            if (nativeFileName == null) {
                return IntPtr.Zero;
            }

            var rid = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "win-x64" : "win-x86";
            // NINA packs natives under runtimes/{rid}/native/{Novas31|}/{NOVASLibraries|SOFALibraries}/{rid}/{file}.
            // NOVAS adds an extra "Novas31" folder; SOFA does not.
            var subFolder = nativeFileName.Contains("novas") ? "Novas31/NOVASLibraries" : "SOFALibraries";
            var candidates = new[] {
                Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", subFolder, rid, nativeFileName),
                Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", nativeFileName),
                Path.Combine(AppContext.BaseDirectory, nativeFileName),
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
