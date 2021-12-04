#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Equipment;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp {

    internal class Program {

        private static void Main(string[] args) {
            /*
            string id = ASCOM.DriverAccess.Telescope.Choose("");
            if (string.IsNullOrEmpty(id))
                return;
            */

            // create this device
            ASCOM.DriverAccess.Telescope device = new ASCOM.DriverAccess.Telescope("ASCOM.tenmicron_mount.Telescope");
            device.Connected = true;

            var mountCommander = new AscomMountCommander(device);
            var mount = new Mount(mountCommander);
            /*
            var productFirmware = mount.GetProductFirmware();
            var mountId = mount.GetId();
            var alignmentModelInfo = mount.GetAlignmentModelInfo();
            var declination = mount.GetDeclination();
            var rightAscension = mount.GetRightAscension();
            var sideOfPier = mount.GetSideOfPier();
            var lst = mount.GetLocalSiderealTime();
            var modelCount = mount.GetModelCount();
            var modelNames = Enumerable.Range(1, modelCount).Select(i => mount.GetModelName(i)).ToList();
            var alignmentStarCount = mount.GetAlignmentStarCount();
            var alignmentStars = Enumerable.Range(1, alignmentStarCount).Select(i => mount.GetAlignmentStarInfo(i)).ToList();
            */
            const string command = ":GUDT#";
            var result = device.CommandString(command, true);
            var parts = result.Split(new char[] { ',' }, 2);
            parts[1] = parts[1].TrimEnd('#');

            // 2021-12-02,23:17:52.56#
            // HH:MM.M#
            // HH:MM:SS#
            // HH:MM:SS.S#
            // HH:MM:SS.SS#

            // MM/DD/YY#
            // MM:DD:YY#
            // YYYY-MM-DD#

            string[] date_formats = {
                "yyyy-MM-dd",
                "MM/dd/yy",
                "MM:dd:yy"
            };
            var datePart = DateTime.ParseExact(parts[0], date_formats, null, DateTimeStyles.None);
            int hours = int.Parse(parts[1].Substring(0, 2));
            int minutes = int.Parse(parts[1].Substring(3, 2));
            int seconds;
            if (parts[1].Length == 7) {
                seconds = int.Parse(parts[1].Substring(6, 1)) * 6;
            } else {
                seconds = int.Parse(parts[1].Substring(6, 2));
            }
            int hundredthSeconds = 0;
            if (parts[1].Length == 10) {
                hundredthSeconds = int.Parse(parts[1].Substring(9, 1)) * 10;
            } else if (parts[1].Length == 11) {
                hundredthSeconds = int.Parse(parts[1].Substring(9, 2));
            }

            var dateResult = new DateTime(datePart.Year, datePart.Month, datePart.Day, hours, minutes, seconds, hundredthSeconds * 10, DateTimeKind.Utc);

            Console.WriteLine(result);
            Console.WriteLine(dateResult);
            Console.WriteLine("Complete");
        }
    }
}