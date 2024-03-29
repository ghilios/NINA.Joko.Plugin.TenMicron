﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace NINA.Joko.Plugin.TenMicron.Equipment {

    public class ModelAccessor : IModelAccessor {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IMountModelMediator mountModelMediator;
        private readonly ICustomDateTime dateTime;

        public ModelAccessor(ITelescopeMediator telescopeMediator, IMountModelMediator mountModelMediator, ICustomDateTime dateTime) {
            this.telescopeMediator = telescopeMediator;
            this.mountModelMediator = mountModelMediator;
            this.dateTime = dateTime;
        }

        public LoadedAlignmentModel LoadActiveModel(string modelName = null, IProgress<ApplicationStatus> progress = null, CancellationToken ct = default) {
            var alignmentModel = new LoadedAlignmentModel();
            LoadActiveModelInto(alignmentModel, modelName, progress, ct);
            return alignmentModel;
        }

        public void LoadActiveModelInto(LoadedAlignmentModel alignmentModel, string modelName = null, IProgress<ApplicationStatus> progress = null, CancellationToken ct = default) {
            try {
                alignmentModel.Clear();

                progress?.Report(new ApplicationStatus() { Status = "Getting Alignment Model" });
                var alignmentStarCount = mountModelMediator.GetAlignmentStarCount();
                ct.ThrowIfCancellationRequested();

                if (alignmentStarCount > 0) {
                    var telescopeInfo = telescopeMediator.GetInfo();
                    alignmentModel.AlignmentStarCount = alignmentStarCount;
                    alignmentModel.ModelName = modelName;
                    alignmentModel.Latitude = Angle.ByDegree(telescopeInfo.SiteLatitude);
                    alignmentModel.Longitude = Angle.ByDegree(telescopeInfo.SiteLongitude);
                    alignmentModel.SiteElevation = telescopeInfo.SiteElevation;

                    var alignmentModelInfo = mountModelMediator.GetAlignmentModelInfo();
                    alignmentModel.RMSError = alignmentModelInfo.RMSError;
                    alignmentModel.RightAscensionAzimuth = alignmentModelInfo.RightAscensionAzimuth;
                    alignmentModel.RightAscensionAltitude = alignmentModelInfo.RightAscensionAltitude;
                    alignmentModel.PolarAlignErrorDegrees = alignmentModelInfo.PolarAlignErrorDegrees;
                    alignmentModel.PAErrorAltitudeDegrees = alignmentModelInfo.RightAscensionAltitude - (decimal)telescopeInfo.SiteLatitude;
                    if ((double)alignmentModelInfo.RightAscensionAzimuth < 180.0d) {
                        alignmentModel.PAErrorAzimuthDegrees = alignmentModelInfo.RightAscensionAzimuth;
                    } else {
                        alignmentModel.PAErrorAzimuthDegrees = alignmentModelInfo.RightAscensionAzimuth - (decimal)360.0d;
                    }

                    alignmentModel.RightAscensionPolarPositionAngleDegrees = alignmentModelInfo.RightAscensionPolarPositionAngleDegrees;
                    alignmentModel.OrthogonalityErrorDegrees = alignmentModelInfo.OrthogonalityErrorDegrees;
                    alignmentModel.AzimuthAdjustmentTurns = alignmentModelInfo.AzimuthAdjustmentTurns;
                    alignmentModel.AltitudeAdjustmentTurns = alignmentModelInfo.AltitudeAdjustmentTurns;
                    alignmentModel.ModelTerms = alignmentModelInfo.ModelTerms;
                    ct.ThrowIfCancellationRequested();

                    var alignmentStars = ImmutableList.CreateBuilder<AlignmentStarInfo>();
                    for (int i = 1; i <= alignmentStarCount; ++i) {
                        progress?.Report(
                            new ApplicationStatus {
                                MaxProgress = alignmentStarCount,
                                Progress = i,
                                Status = $"Getting Alignment Star {i} / {alignmentStarCount}",
                                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue
                            });
                        alignmentStars.Add(mountModelMediator.GetAlignmentStarInfo(i));
                        ct.ThrowIfCancellationRequested();
                    }

                    alignmentModel.OriginalAlignmentStars = alignmentStars.ToImmutable();
                    alignmentModel.SynchronizePoints();
                }
            } catch (Exception) {
                alignmentModel.Clear();
                throw;
            } finally {
                progress?.Report(new ApplicationStatus() { Status = "" });
            }
        }
    }
}