﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugin.TenMicron.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.TenMicron.Interfaces {

    public interface IMountModelBuilderVM : IDockableVM {
        CustomHorizon CustomHorizon { get; }

        Task<bool> BuildModel(IList<ModelPoint> modelPoints, ModelBuilderOptions options, CancellationToken ct);
    }
}