﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Model;

namespace NINA.Joko.Plugin.TenMicron.Interfaces {

    public interface ITenMicronOptions {
        int GoldenSpiralStarCount { get; set; }

        int SiderealTrackStartOffsetSeconds { get; set; }

        int SiderealTrackEndOffsetSeconds { get; set; }

        double SiderealTrackRADeltaDegrees { get; set; }

        int DomeShutterWidth_mm { get; set; }

        bool MinimizeDomeMovementEnabled { get; set; }

        bool WestToEastSorting { get; set; }

        int BuilderNumRetries { get; set; }

        double MaxPointRMS { get; set; }

        bool LogCommands { get; set; }

        bool AllowBlindSolves { get; set; }

        int MaxConcurrency { get; set; }

        bool SyncFirstPoint { get; set; }

        ModelPointGenerationTypeEnum ModelPointGenerationType { get; set; }

        void ResetDefaults();
    }
}