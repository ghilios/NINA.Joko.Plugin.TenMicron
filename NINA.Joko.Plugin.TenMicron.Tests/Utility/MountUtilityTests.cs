using System.Linq;
using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.Utility {

    [TestFixture]
    public class MountUtilityTests {

        private static readonly string[] KnownProducts = new[] {
            "10micron GM1000HPS",
            "10micron GM2000QCI",
            "10micron GM2000HPS",
            "10micron GM3000HPS",
            "10micron GM4000QCI",
            "10micron GM4000QCI 48V",
            "10micron GM4000HPS",
            "10micron AZ2000",
            "10micron AZ2000HPS",
            "10micron AZ4000HPS",
        };

        [TestCaseSource(nameof(KnownProducts))]
        public void IsSupportedProduct_KnownModel_ReturnsTrue(string productName) {
            var fw = new ProductFirmware(productName, System.DateTime.UtcNow, new System.Version(2, 0));

            MountUtility.IsSupportedProduct(fw).Should().BeTrue();
        }

        [Test]
        public void IsSupportedProduct_UnknownModel_ReturnsFalse() {
            var fw = new ProductFirmware("Some Other Mount", System.DateTime.UtcNow, new System.Version(1, 0));

            MountUtility.IsSupportedProduct(fw).Should().BeFalse();
        }

        [Test]
        public void BuildMagicPacket_Returns102Bytes() {
            var packet = MountUtility.BuildMagicPacket("AA:BB:CC:DD:EE:FF");

            packet.Length.Should().Be(6 + 16 * 6);
        }

        [Test]
        public void BuildMagicPacket_First6Bytes_AreAllFF() {
            var packet = MountUtility.BuildMagicPacket("AA:BB:CC:DD:EE:FF");

            packet.Take(6).Should().AllBeEquivalentTo((byte)0xFF);
        }

        [Test]
        public void BuildMagicPacket_RepeatsMacBytes_16Times() {
            var packet = MountUtility.BuildMagicPacket("AA:BB:CC:DD:EE:FF");

            var expectedMac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
            for (int rep = 0; rep < 16; ++rep) {
                var slice = packet.Skip(6 + rep * 6).Take(6).ToArray();
                slice.Should().Equal(expectedMac);
            }
        }

        [TestCase("AA:BB:CC:DD:EE:FF")]
        [TestCase("AA-BB-CC-DD-EE-FF")]
        [TestCase("AA BB CC DD EE FF")]
        [TestCase("AABBCCDDEEFF")]
        public void BuildMagicPacket_AcceptsColonsDashesSpacesOrNoSeparator(string mac) {
            var packet = MountUtility.BuildMagicPacket(mac);
            var reference = MountUtility.BuildMagicPacket("AABBCCDDEEFF");

            packet.Should().Equal(reference);
        }

        [Test]
        public void ValidateMountAscomConfig_EnableUncheckedRaw_ReturnsFalse() {
            var cfg = new MountAscomConfig { EnableUncheckedRawCommands = true };

            // NOTE: Notification.ShowError is invoked by the implementation. In a non-NINA-host process
            // it logs or no-ops — if it throws here, the test fails informatively and we know we need to
            // extract an INotificationService seam.
            MountUtility.ValidateMountAscomConfig(cfg).Should().BeFalse();
        }

        [Test]
        public void ValidateMountAscomConfig_ValidConfig_ReturnsTrue() {
            var cfg = new MountAscomConfig {
                EnableUncheckedRawCommands = false,
                EnableSync = false,
                UseSyncAsAlignment = false,
                UseJ2000Coordinates = false,
            };

            MountUtility.ValidateMountAscomConfig(cfg).Should().BeTrue();
        }
    }
}
