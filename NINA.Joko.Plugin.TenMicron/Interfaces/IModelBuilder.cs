#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Joko.Plugin.TenMicron.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.TenMicron.Interfaces {

    public class ModelBuilderOptions {
        public int NumRetries { get; set; } = 0;
        public double? MaxPointRMS { get; set; } = null;
        public bool WestToEastSorting { get; set; } = false;
        public bool MinimizeDomeMovement { get; set; } = true;
        public bool AllowBlindSolves { get; set; } = false;
        public bool SyncFirstPoint { get; set; } = true;
        public int MaxConcurrency { get; set; } = 3;
        public int DomeShutterWidth_mm { get; set; } = 0;
    }

    public interface IModelBuilder {

        Task<LoadedAlignmentModel> Build(List<ModelPoint> modelPoints, ModelBuilderOptions options, CancellationToken ct = default, IProgress<ApplicationStatus> overallProgress = null, IProgress<ApplicationStatus> stepProgress = null);
    }
}