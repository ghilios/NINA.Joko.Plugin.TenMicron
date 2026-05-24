#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;
using NINA.Joko.Plugin.TenMicron.Interfaces;

namespace NINA.Joko.Plugin.TenMicron.Utility {

    public class AscomProfileAccessor : IAscomProfileAccessor {

        public bool IsRegistered(string driverId) =>
            ASCOM.Com.Profile.IsRegistered(ASCOM.Common.DeviceTypes.Telescope, driverId);

        public string GetValue(string driverId, string valueName, string subKey, string defaultValue) =>
            ASCOM.Com.Profile.GetValue(ASCOM.Common.DeviceTypes.Telescope, driverId, valueName, subKey, defaultValue);

        public Dictionary<string, string> GetValues(string driverId) =>
            ASCOM.Com.Profile.GetValues(ASCOM.Common.DeviceTypes.Telescope, driverId);
    }
}
