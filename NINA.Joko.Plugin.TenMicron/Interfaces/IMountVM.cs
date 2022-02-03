﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Core.Enum;
using System;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Equipment.Interfaces;
using System.Threading.Tasks;
using System.Threading;

namespace NINA.Joko.Plugin.TenMicron.Interfaces {

    public interface IMountVM : IDeviceVM<MountInfo>, IDockableVM {

        CoordinateAngle GetMountReportedDeclination();

        AstrometricTime GetMountReportedRightAscension();

        AstrometricTime GetMountReportedLocalSiderealTime();

        bool SetTrackingRate(TrackingMode trackingMode);

        bool Shutdown();

        Task<bool> PowerOn(CancellationToken ct);
    }
}