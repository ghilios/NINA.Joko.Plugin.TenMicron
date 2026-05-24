using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Utility {

    [TestFixture]
    public class MountUtilityGetAscomConfigTests {

        [Test]
        public void GetMountAscomConfig_UnregisteredDriver_ReturnsNull() {
            var accessor = new Mock<IAscomProfileAccessor>();
            accessor.Setup(a => a.IsRegistered(It.IsAny<string>())).Returns(false);

            var result = MountUtility.GetMountAscomConfig("AnyDriver", accessor.Object);

            result.Should().BeNull();
        }

        [Test]
        public void GetMountAscomConfig_NonTenMicronDriver_ReturnsNull() {
            var accessor = BuildAccessor(registered: true);

            var result = MountUtility.GetMountAscomConfig("ASCOM.SomeOther.Telescope", accessor.Object);

            result.Should().BeNull();
        }

        [Test]
        public void GetMountAscomConfig_TenMicronDriver_ParsesAllSettings() {
            var accessor = BuildAccessor(
                registered: true,
                getValueResults: new Dictionary<(string, string, string), string> {
                    { ("ASCOM.tenmicron_mount.Telescope", "enable_unchecked_raw_commands", "mount_settings"), "False" },
                    { ("ASCOM.tenmicron_mount.Telescope", "use_J2000_coords", "mount_settings"), "True" },
                    { ("ASCOM.tenmicron_mount.Telescope", "enable_sync", "mount_settings"), "True" },
                    { ("ASCOM.tenmicron_mount.Telescope", "use_sync_as_alignment", "mount_settings"), "False" },
                    { ("ASCOM.tenmicron_mount.Telescope", "refraction_update_file", "mount_settings"), "C:/path/file.txt" },
                });

            var result = MountUtility.GetMountAscomConfig("ASCOM.tenmicron_mount.Telescope", accessor.Object);

            result.Should().NotBeNull();
            result.EnableUncheckedRawCommands.Should().BeFalse();
            result.UseJ2000Coordinates.Should().BeTrue();
            result.EnableSync.Should().BeTrue();
            result.UseSyncAsAlignment.Should().BeFalse();
            result.RefractionUpdateFile.Should().Be("C:/path/file.txt");
        }

        [Test]
        public void GetMountAscomConfig_EmptyProfileValues_UsesDefaults() {
            // Accessor returns empty strings -> bool.TryParse fails -> defaults apply.
            var accessor = BuildAccessor(registered: true);

            var result = MountUtility.GetMountAscomConfig("ASCOM.tenmicron_mount.Telescope", accessor.Object);

            result.Should().NotBeNull();
            result.EnableUncheckedRawCommands.Should().BeTrue();  // default
            result.UseJ2000Coordinates.Should().BeFalse();
            result.EnableSync.Should().BeFalse();
            result.UseSyncAsAlignment.Should().BeFalse();
            result.RefractionUpdateFile.Should().Be("");
        }

        // GetValues is independently always stubbed to an empty dictionary because production code
        // only uses its return for JSON logging; only getValueResults drives observable behaviour.
        private static Mock<IAscomProfileAccessor> BuildAccessor(
            bool registered,
            Dictionary<(string, string, string), string> getValueResults = null) {
            getValueResults ??= new Dictionary<(string, string, string), string>();
            var accessor = new Mock<IAscomProfileAccessor>();
            accessor.Setup(a => a.IsRegistered(It.IsAny<string>())).Returns(registered);
            accessor.Setup(a => a.GetValues(It.IsAny<string>())).Returns(new Dictionary<string, string>());
            accessor.Setup(a => a.GetValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string, string, string>((driverId, name, subKey, defaultValue) =>
                    getValueResults.TryGetValue((driverId, name, subKey), out var v) ? v : "");
            return accessor;
        }
    }
}
