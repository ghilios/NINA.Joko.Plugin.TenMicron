﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Runtime.InteropServices;

namespace NINA.Joko.Plugin.TenMicron.Equipment {

    public static class NativeMethods {

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern ulong SendARP(uint DestIP, uint SrcIP, [Out] byte[] pMacAddr, ref ulong PhyAddrLen);
    }
}