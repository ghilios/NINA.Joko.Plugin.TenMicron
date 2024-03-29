﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugin.TenMicron.Equipment {

    public class MountAscomConfig {
        public bool EnableUncheckedRawCommands { get; set; }
        public bool UseJ2000Coordinates { get; set; }
        public bool EnableSync { get; set; }
        public bool UseSyncAsAlignment { get; set; }
        public string RefractionUpdateFile { get; set; }
    }
}