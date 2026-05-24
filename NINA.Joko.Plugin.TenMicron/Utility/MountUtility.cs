#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using System.Collections.Generic;
using System.Collections.Immutable;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.Equipment;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace NINA.Joko.Plugin.TenMicron.Utility {

    public static class MountUtility {

        private static ISet<string> SupportedProducts = ImmutableHashSet.CreateRange(
            new[] {
                "10micron GM1000HPS",
                "10micron GM2000QCI",
                "10micron GM2000HPS",
                "10micron GM3000HPS",
                "10micron GM4000QCI",
                "10micron GM4000QCI 48V",
                "10micron GM4000HPS",
                "10micron AZ2000",
                "10micron AZ2000HPS",
                "10micron AZ4000HPS"
            });

        public static bool IsSupportedProduct(ProductFirmware productFirmware) {
            return SupportedProducts.Contains(productFirmware.ProductName);
        }

        public static MountAscomConfig GetMountAscomConfig(string driverId) =>
            GetMountAscomConfig(driverId, new AscomProfileAccessor());

        internal static MountAscomConfig GetMountAscomConfig(string driverId, IAscomProfileAccessor accessor) {
            if (!accessor.IsRegistered(driverId)) {
                return null;
            }

            var profileJson = JsonConvert.SerializeObject(accessor.GetValues(driverId));
            Logger.Info($"10u ASCOM driver configuration: {profileJson}");

            if (driverId != "ASCOM.tenmicron_mount.Telescope") {
                return null;
            }

            return new MountAscomConfig() {
                EnableUncheckedRawCommands = TryGetBool(accessor, driverId, "enable_unchecked_raw_commands", "mount_settings", true),
                UseJ2000Coordinates = TryGetBool(accessor, driverId, "use_J2000_coords", "mount_settings", false),
                EnableSync = TryGetBool(accessor, driverId, "enable_sync", "mount_settings", false),
                UseSyncAsAlignment = TryGetBool(accessor, driverId, "use_sync_as_alignment", "mount_settings", false),
                RefractionUpdateFile = accessor.GetValue(driverId, "refraction_update_file", "mount_settings", "")
            };
        }

        private static bool TryGetBool(IAscomProfileAccessor accessor, string driverId, string name, string subKey, bool defaultValue) {
            if (bool.TryParse(accessor.GetValue(driverId, name, subKey, ""), out var result)) {
                return result;
            }
            return defaultValue;
        }

        public static bool ValidateMountAscomConfig(MountAscomConfig config) {
            if (config.EnableUncheckedRawCommands) {
                Notification.ShowError("Enable Unchecked Raw Commands cannnot be enabled. Open the ASCOM driver configuration and disable it, then reconnect");
                return false;
            }
            if (config.EnableSync && config.UseSyncAsAlignment) {
                Notification.ShowWarning("Use Sync as Alignment is enabled. It is recommended you disable this setting and build models explicitly");
            }
            if (config.UseJ2000Coordinates) {
                Notification.ShowWarning("ASCOM driver is configured to use J2000 coordinates. It is recommended you use JNow instead and reconnect");
            }
            return true;
        }

        // See: https://stackoverflow.com/questions/861873/wake-on-lan-using-c-sharp
        public static async Task WakeOnLan(string macAddress, string broadcastAddress, CancellationToken ct) {
            byte[] magicPacket = BuildMagicPacket(macAddress);

            List<Task> wakeTasks = new List<Task>();
            if (IPAddress.TryParse(broadcastAddress, out var broadcastIpAddress)) {
                wakeTasks.Add(SendWakeOnLan(IPAddress.Any, broadcastIpAddress, magicPacket, ct));
            }

            foreach (NetworkInterface nics in NetworkInterface.GetAllNetworkInterfaces()) {
                if (!nics.Supports(NetworkInterfaceComponent.IPv4)) {
                    Logger.Debug($"Skipping {nics.Name} since it doesn't support IPv4");
                    continue;
                }
                if (nics.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
                    continue;
                }
                if (!nics.SupportsMulticast) {
                    Logger.Debug($"Skipping {nics.Name} since it doesn't support multicast");
                    continue;
                }

                foreach (UnicastIPAddressInformation address in nics.GetIPProperties().UnicastAddresses) {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) {
                        Logger.Debug($"Skipping {address.Address} on {nics.Name} since it isn't InterNetwork");
                        continue;
                    }
                    Logger.Info($"Sending WOL package to {broadcastAddress} using {address.Address} on {nics.Name}");
                    wakeTasks.Add(SendWakeOnLan(address.Address, broadcastIpAddress, magicPacket, ct));

                    foreach (MulticastIPAddressInformation multicastAddress in nics.GetIPProperties().MulticastAddresses) {
                        Logger.Info($"Sending WOL package to {multicastAddress.Address} using {address.Address} on {nics.Name}");
                        wakeTasks.Add(SendWakeOnLan(address.Address, multicastAddress.Address, magicPacket, ct));
                    }
                }
            }

            await Task.WhenAll(wakeTasks.ToArray());
        }

        internal static byte[] BuildMagicPacket(string macAddress) {
            macAddress = Regex.Replace(macAddress, "[: -]", "");
            byte[] macBytes = new byte[6];
            for (int i = 0; i < 6; i++) {
                macBytes[i] = Convert.ToByte(macAddress.Substring(i * 2, 2), 16);
            }

            using (var ms = new MemoryStream()) {
                using (var bw = new BinaryWriter(ms)) {
                    for (int i = 0; i < 6; i++) {
                        bw.Write((byte)0xff);
                    }
                    for (int i = 0; i < 16; i++) {
                        bw.Write(macBytes);
                    }
                }
                return ms.ToArray();
            }
        }

        private static async Task SendWakeOnLan(IPAddress localIpAddress, IPAddress multicastIpAddress, byte[] magicPacket, CancellationToken ct) {
            var port = 9;
            try {
                var localEndPoint = new IPEndPoint(localIpAddress, 0);
                using (var client = new UdpClient()) {
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                    client.ExclusiveAddressUse = false;
                    client.Client.Bind(localEndPoint);
                    client.EnableBroadcast = true;
                    using (ct.Register(() => client.Close())) {
                        await client.SendAsync(magicPacket, magicPacket.Length, multicastIpAddress.ToString(), port);
                    }
                }
            } catch (Exception e) {
                Logger.Warning($"Failed to send WOL packet using local IP {localIpAddress} and multicast IP {multicastIpAddress}. Error: {e}");
            }
        }

        public static async Task<bool> IsResponding(IPAddress ipAddress, int port, CancellationToken ct) {
            try {
                using (var client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)) {
                    using (ct.Register(() => client.Close())) {
                        client.SendTimeout = 2000;
                        client.ReceiveTimeout = 1000;

                        await client.ConnectAsync(ipAddress, port);
                        ct.ThrowIfCancellationRequested();

                        var command = ":GJD#";
                        var commandData = Encoding.ASCII.GetBytes(command);
                        var sentBytes = await client.SendAsync(new ArraySegment<byte>(commandData), SocketFlags.None);
                        ct.ThrowIfCancellationRequested();

                        // 14 bytes expected: JJJJJJJ.JJJJJ#
                        var receivedData = new byte[14];
                        var receivedBytes = await client.ReceiveAsync(new ArraySegment<byte>(receivedData), SocketFlags.None);
                        if (receivedBytes > 0) {
                            return true;
                        }
                    }
                }
            } catch (Exception) {
                return false;
            }
            return false;
        }

        public static async Task<bool> WaitUntilResponding(IPAddress ipAddress, int port, CancellationToken ct) {
            while (true) {
                ct.ThrowIfCancellationRequested();

                if (await IsResponding(ipAddress, port, ct)) {
                    return true;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }
}