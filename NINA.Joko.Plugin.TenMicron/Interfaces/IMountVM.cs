﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.ModelBuilder;
using NINA.Equipment.Interfaces.ViewModel;

namespace NINA.Joko.Plugin.TenMicron.Interfaces {

    public interface IMountVM : IDeviceVM<MountInfo>, IDockableVM {

        string GetModelName(int modelIndex);

        int GetModelCount();

        bool LoadModel(string name);

        bool SaveModel(string name);

        bool DeleteModel(string name);

        void DeleteAlignment();

        bool DeleteAlignmentStar(int alignmentStarIndex);

        int GetAlignmentStarCount();

        AlignmentStarInfo GetAlignmentStarInfo(int alignmentStarIndex);

        AlignmentModelInfo GetAlignmentModelInfo();

        bool StartNewAlignmentSpec();

        bool FinishAlignmentSpec();

        CoordinateAngle GetMountReportedDeclination();

        AstrometricTime GetMountReportedRightAscension();

        AstrometricTime GetMountReportedLocalSiderealTime();
    }
}