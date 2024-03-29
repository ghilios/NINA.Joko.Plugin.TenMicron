﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Joko.Plugin.TenMicron.Exceptions;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.Utility;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.TenMicron.ModelManagement {

    public class ModelBuilder : IModelBuilder {
        private static IComparer<double> DOUBLE_COMPARER = Comparer<double>.Default;

        private readonly IMount mount;
        private readonly IMountModelMediator mountModelMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IWeatherDataMediator weatherDataMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeSynchronization domeSynchronization;
        private readonly IProfileService profileService;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly ICustomDateTime nowProvider = new SystemDateTime();
        private volatile int processingInProgressCount;

        public event EventHandler<PointNextUpEventArgs> PointNextUp;

        public ModelBuilder(
            IProfileService profileService, IMountModelMediator mountModelMediator, IMount mount, ITelescopeMediator telescopeMediator, IDomeMediator domeMediator, ICameraMediator cameraMediator,
            IDomeSynchronization domeSynchronization, IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator,
            IWeatherDataMediator weatherDataMediator) {
            this.mountModelMediator = mountModelMediator;
            this.imagingMediator = imagingMediator;
            this.mount = mount;
            this.telescopeMediator = telescopeMediator;
            this.cameraMediator = cameraMediator;
            this.domeMediator = domeMediator;
            this.domeSynchronization = domeSynchronization;
            this.weatherDataMediator = weatherDataMediator;
            this.profileService = profileService;
            this.plateSolverFactory = plateSolverFactory;
            this.filterWheelMediator = filterWheelMediator;
        }

        private class ModelBuilderState {

            public ModelBuilderState(ModelBuilderOptions options, IList<ModelPoint> modelPoints, IMount mount, IDomeMediator domeMediator, IWeatherDataMediator weatherDataMediator, ICameraMediator cameraMediator, IProfileService profileService) {
                this.Options = options;
                var maxConcurrent = options.MaxConcurrency > 0 ? options.MaxConcurrency : int.MaxValue;
                this.ProcessingSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
                this.ModelPoints = ImmutableList.ToImmutableList(modelPoints);
                this.ValidPoints = ImmutableList.ToImmutableList(modelPoints.Where(p => p.ModelPointState != ModelPointStateEnum.BelowHorizon && p.ModelPointState != ModelPointStateEnum.OutsideAltitudeBounds && p.ModelPointState != ModelPointStateEnum.OutsideAzimuthBounds));
                this.PendingTasks = new List<Task<bool>>();

                var domeInfo = domeMediator.GetInfo();
                this.UseDome = domeInfo?.Connected == true && domeInfo?.CanSetAzimuth == true;
                this.PointAzimuthComparer = GetPointComparer(this.UseDome, options, false);

                var refractionCorrectionEnabled = mount.GetRefractionCorrectionEnabled();
                this.PressurehPa = refractionCorrectionEnabled ? (double)mount.GetPressure().Value : 0.0d;
                this.Temperature = refractionCorrectionEnabled ? (double)mount.GetTemperature().Value : 0.0d;
                this.Wavelength = refractionCorrectionEnabled ? 0.55d : 0.0d;
                this.Humidity = 0.0d;
                if (refractionCorrectionEnabled) {
                    var weatherDataInfo = weatherDataMediator.GetInfo();
                    if (weatherDataInfo.Connected) {
                        var reportedHumidity = weatherDataInfo.Humidity;
                        if (!double.IsNaN(reportedHumidity)) {
                            this.Humidity = reportedHumidity;
                        }
                    }
                }

                if (options.PlateSolveSubframePercentage < 0.99d) {
                    var cameraInfo = cameraMediator.GetInfo();
                    if (!cameraInfo.CanSubSample) {
                        Notification.ShowWarning("Camera does not support subsampling. Model building will use the entire frame");
                    } else {
                        var binning = profileService.ActiveProfile.PlateSolveSettings.Binning;

                        Logger.Info($"Using {options.PlateSolveSubframePercentage * 100.0d}% subsampling with {binning}x binning for plate solves.");
                        var fullWidth = cameraInfo.XSize / binning;
                        var fullHeight = cameraInfo.YSize / binning;
                        var startX = (1.0d - options.PlateSolveSubframePercentage) / 2.0d * fullWidth;
                        var startY = (1.0d - options.PlateSolveSubframePercentage) / 2.0d * fullHeight;
                        var width = options.PlateSolveSubframePercentage * fullWidth;
                        var height = options.PlateSolveSubframePercentage * fullHeight;
                        this.PlateSolveSubsample = new ObservableRectangle(x: startX, y: startY, width: width, height: height);
                    }
                }
            }

            public void ReverseAzimuthDirectionIfNecessary() {
                if (Options.AlternateDirectionsBetweenIterations) {
                    DirectionReversed = !DirectionReversed;
                    this.PointAzimuthComparer = GetPointComparer(this.UseDome, Options, DirectionReversed);
                }
            }

            public Separation SyncSeparation { get; set; } = null;
            public DateTime IterationStartTime { get; set; }
            public ModelBuilderOptions Options { get; private set; }
            public ImmutableList<ModelPoint> ModelPoints { get; private set; }
            public ImmutableList<ModelPoint> ValidPoints { get; private set; }
            public ImmutableList<ModelPoint> BestModelPoints { get; set; } = ImmutableList.Create<ModelPoint>();
            public double BestModelRMS { get; set; } = double.PositiveInfinity;
            public SemaphoreSlim ProcessingSemaphore { get; private set; }
            public List<Task<bool>> PendingTasks { get; private set; }
            public IComparer<ModelPoint> PointAzimuthComparer { get; private set; }
            public bool RefractionCorrectionEnabled { get; private set; }
            public bool UseDome { get; private set; }
            public double PressurehPa { get; private set; }
            public double Temperature { get; private set; }
            public double Humidity { get; private set; }
            public double Wavelength { get; private set; }
            public Task<bool> DomeSlewTask { get; set; }
            public int BuildAttempt { get; set; }
            public int PriorSuccessfulPointsProcessed { get; set; }
            public int PointsProcessed { get; set; }
            public int FailedPoints { get; set; }
            public bool IsComplete { get; set; } = false;
            public ObservableRectangle PlateSolveSubsample { get; set; } = null;
            public bool DirectionReversed { get; private set; } = false;

            private static IComparer<ModelPoint> GetPointComparer(bool useDome, ModelBuilderOptions options, bool reversed) {
                bool westToEast = options.WestToEastSorting ^ reversed;
                if (useDome && options.MinimizeDomeMovement) {
                    return Comparer<ModelPoint>.Create(
                        (mp1, mp2) => {
                            if (!westToEast) {
                                var bound1 = double.IsNaN(mp1.MinDomeAzimuth) ? double.MinValue : mp1.MinDomeAzimuth;
                                var bound2 = double.IsNaN(mp2.MinDomeAzimuth) ? double.MinValue : mp2.MinDomeAzimuth;
                                return DOUBLE_COMPARER.Compare(bound1, bound2);
                            } else {
                                var bound1 = double.IsNaN(mp1.MaxDomeAzimuth) ? double.MaxValue : mp1.MaxDomeAzimuth;
                                var bound2 = double.IsNaN(mp2.MaxDomeAzimuth) ? double.MaxValue : mp2.MaxDomeAzimuth;
                                return DOUBLE_COMPARER.Compare(bound2, bound1);
                            }
                        });
                } else {
                    return Comparer<ModelPoint>.Create(
                        (mp1, mp2) => !westToEast ? DOUBLE_COMPARER.Compare(mp1.Azimuth, mp2.Azimuth) : DOUBLE_COMPARER.Compare(mp2.Azimuth, mp1.Azimuth));
                }
            }
        }

        public async Task<LoadedAlignmentModel> Build(IList<ModelPoint> modelPoints, ModelBuilderOptions options, CancellationToken ct = default, CancellationToken stopToken = default, IProgress<ApplicationStatus> overallProgress = null, IProgress<ApplicationStatus> stepProgress = null) {
            ct.ThrowIfCancellationRequested();
            PreFlightChecks(modelPoints);

            var innerCts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, innerCts.Token);
            var telescopeInfo = telescopeMediator.GetInfo();
            var startedAtPark = telescopeInfo.AtPark;
            var startCoordinates = telescopeInfo.Coordinates;
            if (startedAtPark) {
                Logger.Info("Unparking telescope");
                Notification.ShowInformation("Unparked telescope to build 10u model");
                if (!await telescopeMediator.UnparkTelescope(stepProgress, ct)) {
                    throw new Exception("Could not unpark telescope");
                }
            }

            var oldFilter = filterWheelMediator.GetInfo()?.SelectedFilter;
            if (oldFilter != null) {
                Logger.Info($"Filter before building model set to {oldFilter.Name}, and will be restored after completion");
            }

            var reenableDomeFollower = false;
            var state = new ModelBuilderState(options, modelPoints, mount, domeMediator, weatherDataMediator, cameraMediator, profileService);
            if (state.UseDome && domeMediator.IsFollowingScope) {
                if (!await domeMediator.DisableFollowing(ct)) {
                    Logger.Warning("Failed to disable dome follower after 10u model build");
                    Notification.ShowWarning("Failed to disable dome follower after 10u model build");
                }
                reenableDomeFollower = true;
            }

            bool reenableDomeSyncSlew = false;
            if (state.UseDome && profileService.ActiveProfile.DomeSettings.SyncSlewDomeWhenMountSlews) {
                profileService.ActiveProfile.DomeSettings.SyncSlewDomeWhenMountSlews = false;
                reenableDomeSyncSlew = true;
            }

            if (reenableDomeFollower || reenableDomeSyncSlew) {
                Notification.ShowInformation("Stopping dome follower to build 10u model. It will be turned back on after completion");
            }

            bool reenableRefractionCorrection = false;
            if (options.DisableRefractionCorrection && mount.GetRefractionCorrectionEnabled()) {
                if (mount.SetRefractionCorrection(false)) {
                    Notification.ShowInformation("Disabled refraction correction to build 10u model. It will be turned back on after completion");
                    Logger.Info("Disabled refraction correction");
                    reenableRefractionCorrection = true;
                } else {
                    Notification.ShowInformation("Failed to disable refraction correction 10u model build. Continuing");
                    Logger.Warning("Failed to disable refraction correction 10u model build. Continuing");
                }
            }

            try {
                return await DoBuild(state, linkedCts.Token, stopToken, overallProgress, stepProgress);
            } finally {
                state.IsComplete = true;
                PointNextUp?.Invoke(this, new PointNextUpEventArgs() { Point = null });
                if (startedAtPark) {
                    Notification.ShowInformation("Re-parking telescope after 10u model build");
                    await telescopeMediator.ParkTelescope(stepProgress, innerCts.Token);
                } else if (startCoordinates != null) {
                    Notification.ShowInformation("Restoring telescope position after 10u model build");
                    await telescopeMediator.SlewToCoordinatesAsync(startCoordinates, innerCts.Token);
                }
                if (oldFilter != null) {
                    Logger.Info($"Restoring filter to {oldFilter} after 10u model build");
                    await filterWheelMediator.ChangeFilter(oldFilter, progress: stepProgress);
                }

                if (reenableDomeFollower) {
                    if (!await domeMediator.EnableFollowing(innerCts.Token)) {
                        Logger.Warning("Failed to re-enable dome follower after 10u model build");
                        Notification.ShowWarning("Failed to re-enable dome follower after 10u model build");
                    } else {
                        Logger.Info("Re-enabled dome follower after 10u model build");
                    }
                }

                if (reenableDomeSyncSlew) {
                    profileService.ActiveProfile.DomeSettings.SyncSlewDomeWhenMountSlews = true; ;
                    Logger.Info("Re-enabled dome sync slew after 10u model build");
                }

                if (reenableRefractionCorrection) {
                    if (mount.SetRefractionCorrection(true)) {
                        Logger.Info("Re-enabled refraction correction");
                    } else {
                        Logger.Warning("Failed to re-enable refraction correction after 10u model build");
                        Notification.ShowWarning("Failed to re-enable refraction correction after 10u model build");
                    }
                }

                overallProgress?.Report(new ApplicationStatus() { });
                stepProgress?.Report(new ApplicationStatus() { });
                state.ProcessingSemaphore?.Dispose();
                // Make sure any remaining tasks are cancelled, just in case an exception left some remaining work in progress
                innerCts.Cancel();
            }
        }

        private void PreFlightChecks(IList<ModelPoint> modelPoints) {
            var telescopeInfo = telescopeMediator.GetInfo();
            if (!telescopeInfo.Connected) {
                throw new Exception("No telescope connected");
            }

            var cameraInfo = cameraMediator.GetInfo();
            if (!cameraInfo.Connected) {
                throw new Exception("No camera connected");
            }

            ValidateRequest(modelPoints);
        }

        private void StartProgressReporter(ModelBuilderState state, CancellationToken ct, IProgress<ApplicationStatus> overallProgress) {
            _ = Task.Run(async () => {
                while (!ct.IsCancellationRequested && !state.IsComplete) {
                    ReportOverallProgress(state, overallProgress);
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            });
        }

        private async Task<LoadedAlignmentModel> DoBuild(ModelBuilderState state, CancellationToken ct, CancellationToken stopToken, IProgress<ApplicationStatus> overallProgress, IProgress<ApplicationStatus> stepProgress) {
            ct.ThrowIfCancellationRequested();
            var validPoints = state.ValidPoints;
            var options = state.Options;

            var stopOrCancelCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stopToken);
            var stopOrCancelCt = stopOrCancelCts.Token;

            // Pre-Step 1: Clear state for all points except those below the horizon
            PreStep1_ClearState(state);
            processingInProgressCount = 0;

            // Pre-Step 3: If a dome is connected, pre-compute all dome ranges since we're using a fixed Alt/Az for each point
            PreStep3_CacheDomeAzimuthRanges(state);

            StartProgressReporter(state, stopOrCancelCt, overallProgress);

            int retryCount = -1;
            LoadedAlignmentModel builtModel = null;
            try {
                while (retryCount++ < options.NumRetries) {
                    state.PriorSuccessfulPointsProcessed = 0;
                    state.FailedPoints = 0;
                    state.PointsProcessed = 0;
                    state.BuildAttempt = retryCount + 1;
                    state.IterationStartTime = DateTime.Now;
                    state.PendingTasks.Clear();

                    // For these first few steps, only abort if cancel is requested. This way a stop can leave a valid model, if possible
                    ct.ThrowIfCancellationRequested();
                    Logger.Info($"Starting model build iteration {retryCount + 1}");

                    // Step 1: Clear alignment model
                    Logger.Info("Deleting current alignment model");
                    this.mountModelMediator.DeleteAlignment();
                    ct.ThrowIfCancellationRequested();

                    // Step 2: Start new alignment model
                    Logger.Info("Starting new alignment spec");
                    if (!this.mountModelMediator.StartNewAlignmentSpec()) {
                        throw new ModelBuildException("Failed to start new alignment spec");
                    }

                    // Step 3: Add all successful points and clear failed points, which are applicable for retries
                    Step3_PrepareRetryPoints(state, ct);

                    // From here on we can abort with either stop or cancel
                    stopOrCancelCt.ThrowIfCancellationRequested();

                    // Step 4: Process points based on ordering. If dome is involved, it is the point with the least minimum azimuth range or the largest maximum azimuth range, based on E/W ordering and whether MinimizeDomeMovement is enabled
                    await ProcessPoints(state, stopOrCancelCt, overallProgress, stepProgress);
                    stopOrCancelCt.ThrowIfCancellationRequested();

                    // Step 5: Wait for remaining pending processing tasks
                    await WaitForProcessing(state.PendingTasks, stopOrCancelCt, stepProgress);
                    stopOrCancelCt.ThrowIfCancellationRequested();

                    var numPendingFailures = state.PendingTasks.Select(pt => pt.Result).Count(x => !x);
                    Logger.Info($"{numPendingFailures} failures during post-capture processing");
                    state.FailedPoints += numPendingFailures;

                    // Now that we're through with the work, we only abort on cancellation (not stop)
                    builtModel = await FinishAlignment(state, ct);
                    if ((double)builtModel.RMSError < state.BestModelRMS) {
                        state.BestModelPoints = state.ValidPoints.Select(p => p.Clone()).ToImmutableList();
                        state.BestModelRMS = (double)builtModel.RMSError;
                    }

                    var retriesRemaining = options.NumRetries - state.BuildAttempt + 1;
                    if (state.FailedPoints == 0) {
                        Logger.Info($"No failed points remaining after build iteration {state.BuildAttempt}");
                        break;
                    } else if (state.Options.MaxFailedPoints > 0 && state.FailedPoints > state.Options.MaxFailedPoints) {
                        if (retriesRemaining > 0) {
                            Logger.Info($"{state.FailedPoints} failed point exceeds limit of {state.Options.MaxFailedPoints}. Resetting all points to Generated to force retrying all points");
                            Notification.ShowWarning($"{state.FailedPoints} failed points exceeds limit. Retrying all points");
                            foreach (var point in state.ValidPoints) {
                                point.ModelPointState = ModelPointStateEnum.Generated;
                            }

                            state.ReverseAzimuthDirectionIfNecessary();
                        } else {
                            Logger.Warning($"{state.FailedPoints} failed point exceeds limit of {state.Options.MaxFailedPoints}. No retries remaining, so moving on");
                            Notification.ShowWarning($"{state.FailedPoints} failed points with no retries remaining. Keeping all points and moving on.");
                        }
                    } else {
                        var numFailedRMSPoints = state.ValidPoints.Count(p => p.ModelPointState == ModelPointStateEnum.FailedRMS);
                        Logger.Info($"{state.FailedPoints} failed points ({numFailedRMSPoints} with high RMS) during model build iteration {state.BuildAttempt}. {retriesRemaining} retries remaining");
                        if (retriesRemaining > 0) {
                            Notification.ShowInformation($"Retrying 10u model build for {state.FailedPoints} points");
                            state.ReverseAzimuthDirectionIfNecessary();
                        } else {
                            if ((double)builtModel.RMSError > state.BestModelRMS) {
                                Notification.ShowInformation($"Restoring earlier build iteration that produced a lower RMS model");
                                for (int i = 0; i < state.ValidPoints.Count; ++i) {
                                    state.ValidPoints[i].CopyFrom(state.BestModelPoints[i]);
                                }

                                builtModel = await FinishAlignment(state, ct);
                            }

                            numFailedRMSPoints = state.ValidPoints.Count(p => p.ModelPointState == ModelPointStateEnum.FailedRMS);
                            if (options.RemoveHighRMSPointsAfterBuild && numFailedRMSPoints > 0) {
                                // Stop showing progress during final point removal and model query
                                state.IsComplete = true;
                                Notification.ShowInformation($"Removing {numFailedRMSPoints} points with RMS > {options.MaxPointRMS}");

                                foreach (var point in state.ValidPoints) {
                                    // Reset model indexes in preparation for recreating model from existing points
                                    point.ModelIndex = -1;
                                }

                                Logger.Info("Deleting current alignment model to recreate it with remaining points");
                                this.mountModelMediator.DeleteAlignment();

                                Logger.Info("Starting new alignment spec to recreate it with remaining points");
                                if (!this.mountModelMediator.StartNewAlignmentSpec()) {
                                    throw new ModelBuildException("Failed to start new alignment spec");
                                }

                                Logger.Info("Adding remaining points back to model");
                                AddSuccessfulPointsToModel(state, ct);

                                builtModel = await FinishAlignment(state, ct, overrideMaxPointRMS: double.PositiveInfinity);
                            }
                        }
                    }
                }
            } catch (OperationCanceledException) {
                if (stopToken.IsCancellationRequested) {
                    Notification.ShowInformation("10u model build stopped");
                    builtModel = await FinishAlignment(state, ct);
                } else {
                    throw;
                }
            }

            return builtModel;
        }

        private async Task<LoadedAlignmentModel> FinishAlignment(ModelBuilderState state, CancellationToken ct, double? overrideMaxPointRMS = null) {
            var completedPoints = state.ValidPoints.Count - state.FailedPoints;
            if (completedPoints > 2) {
                Logger.Info("Completing alignment spec");
                if (!mountModelMediator.FinishAlignmentSpec()) {
                    Logger.Error("Failed to complete alignment spec. Aborting");
                    return null;
                }

                var maxPointRMS = overrideMaxPointRMS ?? state.Options.MaxPointRMS;
                var builtModel = await mountModelMediator.GetLoadedAlignmentModel(ct);
                Logger.Info($"Completed alignment spec. RMSError={builtModel.RMSError}, Stars={builtModel.AlignmentStarCount}");

                ct.ThrowIfCancellationRequested();
                var modelAlignmentStars = builtModel.AlignmentStars.ToArray();
                foreach (var point in state.ValidPoints) {
                    if (point.ModelPointState == ModelPointStateEnum.AddedToModel) {
                        if (point.ModelIndex > 0 && point.ModelIndex <= modelAlignmentStars.Length) {
                            point.RMSError = modelAlignmentStars[point.ModelIndex - 1].ErrorArcsec;
                            if (!double.IsNaN(state.Options.MaxPointRMS) && point.RMSError > maxPointRMS) {
                                Logger.Info($"Point {point} exceeds limit of {state.Options.MaxPointRMS}. This point will be reattempted if there are remaining retries");
                                point.ModelPointState = ModelPointStateEnum.FailedRMS;
                                ++state.FailedPoints;
                            } else {
                                Logger.Info($"Point {point} added to model");
                            }
                        } else {
                            Logger.Error($"Point {point} has invalid model index {point.ModelIndex}. There are {modelAlignmentStars.Length} alignment stars in the model");
                            point.ModelPointState = ModelPointStateEnum.Failed;
                            ++state.FailedPoints;
                        }
                    }
                }
                return builtModel;
            } else {
                Logger.Error("Not enough successful points to complete alignment spec");
                Notification.ShowError("Not enough successful points to complete alignment spec");
                return null;
            }
        }

        private void PreStep1_ClearState(ModelBuilderState state) {
            foreach (var point in state.ValidPoints) {
                point.ModelIndex = -1;
                point.ModelPointState = ModelPointStateEnum.Generated;
                point.MountReportedDeclination = CoordinateAngle.ZERO;
                point.MountReportedRightAscension = AstrometricTime.ZERO;
                point.MountReportedLocalSiderealTime = AstrometricTime.ZERO;
                point.PlateSolvedCoordinates = null;
                point.MountReportedSideOfPier = PierSide.pierUnknown;
                point.PlateSolvedDeclination = CoordinateAngle.ZERO;
                point.PlateSolvedRightAscension = AstrometricTime.ZERO;
            }
        }

        private void PreStep3_CacheDomeAzimuthRanges(ModelBuilderState state) {
            if (!state.UseDome) {
                return;
            }

            Logger.Info("Dome with settable azimuth connected. Precomputing target dome azimuth ranges");
            var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
            var longitudeDegrees = profileService.ActiveProfile.AstrometrySettings.Longitude;
            var longitude = Angle.ByDegree(longitudeDegrees);
            var domeRadius = profileService.ActiveProfile.DomeSettings.DomeRadius_mm;
            var domeThreshold = Angle.ByDegree(profileService.ActiveProfile.DomeSettings.AzimuthTolerance_degrees);
            var lst = AstroUtil.GetLocalSiderealTimeNow(longitudeDegrees);
            foreach (var modelPoint in state.ValidPoints.Where(IsPointEligibleForBuild).ToList()) {
                // Use celestial coordinates that have not been adjusted for refraction to calculate dome azimuth. This ensures we get a logical RA/Dec that points to the physical location, especially if refraction correction is on
                var celestialCoordinates = modelPoint.ToTopocentric(nowProvider).Transform(Epoch.JNOW);
                if (state.SyncSeparation != null) {
                    celestialCoordinates += state.SyncSeparation;
                }

                var sideOfPier = MeridianFlip.ExpectedPierSide(celestialCoordinates, Angle.ByHours(lst));
                var targetDomeCoordinates = domeSynchronization.TargetDomeCoordinates(celestialCoordinates, lst, siteLatitude: latitude, siteLongitude: longitude, sideOfPier: sideOfPier);
                var domeAzimuth = targetDomeCoordinates.Azimuth;
                Angle minAzimuth, maxAzimuth;
                if (state.Options.DomeShutterWidth_mm > 0) {
                    (minAzimuth, maxAzimuth) = DomeUtility.CalculateDomeAzimuthRange(targetDomeCoordinates.Altitude, targetDomeCoordinates.Azimuth, domeRadius, state.Options.DomeShutterWidth_mm);
                } else {
                    minAzimuth = domeAzimuth - domeThreshold;
                    maxAzimuth = domeAzimuth + domeThreshold;
                }

                Logger.Debug($"Point at Alt={modelPoint.Altitude}, Az={modelPoint.Azimuth} expects side of pier {sideOfPier} and requires dome azimuth between [{AstroUtil.EuclidianModulus(minAzimuth.Degree, 360.0d)}, {AstroUtil.EuclidianModulus(maxAzimuth.Degree, 360.0d)}]");
                modelPoint.MinDomeAzimuth = minAzimuth.Degree;
                modelPoint.MaxDomeAzimuth = maxAzimuth.Degree;
                modelPoint.DomeAzimuth = domeAzimuth.Degree;
                modelPoint.DomeAltitude = targetDomeCoordinates.Altitude.Degree;
                modelPoint.ExpectedDomeSideOfPier = sideOfPier;
            }
        }

        private void Step3_PrepareRetryPoints(ModelBuilderState state, CancellationToken ct) {
            var validPoints = state.ValidPoints;
            var existingFailedPoints = validPoints.Where(p => p.ModelPointState == ModelPointStateEnum.Failed || p.ModelPointState == ModelPointStateEnum.FailedRMS).ToList();
            if (existingFailedPoints.Count > 0) {
                Logger.Info($"Resetting {existingFailedPoints.Count} previously failed points to Generated so they can be reattempted");
                foreach (var point in existingFailedPoints) {
                    point.ModelPointState = ModelPointStateEnum.Generated;
                }
            }

            AddSuccessfulPointsToModel(state, ct);
        }

        private void AddSuccessfulPointsToModel(ModelBuilderState state, CancellationToken ct) {
            var validPoints = state.ValidPoints;
            var existingSuccessfulPoints = validPoints.Where(p => p.ModelPointState == ModelPointStateEnum.AddedToModel).ToList();
            if (existingSuccessfulPoints.Count > 0) {
                Logger.Info($"Adding {existingSuccessfulPoints.Count} previously successful points to the new alignment spec");
                foreach (var point in existingSuccessfulPoints) {
                    ct.ThrowIfCancellationRequested();
                    if (!AddModelPointToAlignmentSpec(point)) {
                        Logger.Error($"Failed to add point {point} during retry. Changing to failed state");
                        ++state.FailedPoints;
                        point.ModelPointState = ModelPointStateEnum.Failed;
                    }
                    ++state.PriorSuccessfulPointsProcessed;
                }
            }
        }

        private async Task<bool> SlewTelescopeToPoint(ModelBuilderState state, ModelPoint point, CancellationToken ct) {
            // Instead of issuing an AltAz slew directly (which requires direct communication with the mount), calculate refraction-adjusted RA/Dec coordinates and slew there instead
            var nextPointCoordinates = point.ToCelestial(pressurehPa: state.PressurehPa, tempCelcius: state.Temperature, relativeHumidity: state.Humidity, wavelength: state.Wavelength, dateTime: nowProvider);
            if (state.SyncSeparation != null) {
                var nextPointCoordinatesAdjusted = nextPointCoordinates + state.SyncSeparation;
                Logger.Info($"Adjusted {nextPointCoordinates} to {nextPointCoordinatesAdjusted}");
                nextPointCoordinates = nextPointCoordinatesAdjusted;
            }

            Logger.Info($"Slewing to {nextPointCoordinates} for point at Alt={point.Altitude:0.###}, Az={point.Azimuth:0.###}");
            return await this.telescopeMediator.SlewToCoordinatesAsync(nextPointCoordinates, ct);
        }

        private async Task ProcessPoints(
            ModelBuilderState state,
            CancellationToken ct,
            IProgress<ApplicationStatus> overallProgress,
            IProgress<ApplicationStatus> stepProgress) {
            var eligiblePoints = state.ValidPoints.Where(IsPointEligibleForBuild).ToList();
            var nextPoint = eligiblePoints.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
            PointNextUp?.Invoke(this, new PointNextUpEventArgs() { Point = nextPoint });

            Logger.Info($"Processing {eligiblePoints.Count} points. First point Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}, MinDomeAz={nextPoint.MinDomeAzimuth:0.###}, MaxDomeAz={nextPoint.MaxDomeAzimuth:0.###}");
            if (state.UseDome) {
                _ = SlewDomeIfNecessary(state, eligiblePoints, ct);
            }

            while (nextPoint != null) {
                ct.ThrowIfCancellationRequested();
                nextPoint.ModelPointState = ModelPointStateEnum.UpNext;

                bool success = false;
                try {
                    if (!await SlewTelescopeToPoint(state, nextPoint, ct)) {
                        Logger.Error($"Failed to slew to {nextPoint}. Continuing to the next point");
                        nextPoint.ModelPointState = ModelPointStateEnum.Failed;
                        ++state.FailedPoints;
                    } else {
                        using (MyStopWatch.Measure("Waiting on ProcessingSemaphore")) {
                            await state.ProcessingSemaphore.WaitAsync(ct);
                        }

                        try {
                            if (state.UseDome) {
                                var localDomeSlewTask = state.DomeSlewTask;
                                if (localDomeSlewTask != null) {
                                    Logger.Info("Waiting for dome slew before starting image capture");
                                    await localDomeSlewTask;
                                }

                                var mountReportedSideOfPier = mount.GetSideOfPier();
                                if (mountReportedSideOfPier != nextPoint.ExpectedDomeSideOfPier) {
                                    Notification.ShowWarning($"Mount pier is on {mountReportedSideOfPier}, but the dome calculation expected {nextPoint.ExpectedDomeSideOfPier}. Point will likely fail. Please report this issue");
                                    Logger.Warning($"Mount pier is on {mountReportedSideOfPier}, but the dome calculation expected {nextPoint.ExpectedDomeSideOfPier}");
                                }

                                var domeInfo = domeMediator.GetInfo();
                                if (domeInfo.Slewing) {
                                    Notification.ShowWarning("Dome is still slewing after we expected it to be complete. Check that other systems aren't interfering with the dome");
                                    Logger.Warning("Dome is still slewing after we expected it to be complete");
                                }

                                if (!IsPointVisibleThroughDome(nextPoint, 1.0d)) {
                                    Notification.ShowWarning("Next point isn't expected to be visible through the dome. Point will likely fail.");
                                    Logger.Warning("Next point isn't expected to be visible through the dome. Point will likely fail.");
                                }
                            }

                            // Successfully slewed to point. Take an exposure
                            var exposureData = await CaptureImage(state, nextPoint, stepProgress, ct);
                            ct.ThrowIfCancellationRequested();
                            if (exposureData == null) {
                                Logger.Error("Failed to take exposure. Continuing to the next point");
                            } else {
                                var completeProcessTask = SolveAndCompleteProcessing(state, nextPoint, exposureData, ct);
                                state.PendingTasks.Add(completeProcessTask);
                                success = true;
                            }
                        } catch (OperationCanceledException) {
                            throw;
                        } catch (Exception e) {
                            Logger.Error(e, "Error during Capture + Processing. Releasing processing semaphore and moving on");
                            nextPoint.ModelPointState = ModelPointStateEnum.Failed;
                            state.ProcessingSemaphore.Release();
                        }
                    }
                } catch (OperationCanceledException) {
                    nextPoint.ModelPointState = ModelPointStateEnum.Failed;
                    ++state.FailedPoints;
                    throw;
                } catch (Exception e) {
                    Logger.Error(e, $"Error while processing {nextPoint} for model build");
                    success = false;
                }

                ++state.PointsProcessed;
                if (!success) {
                    nextPoint.ModelPointState = ModelPointStateEnum.Failed;
                    ++state.FailedPoints;
                }

                // Refresh dome azimuth ranges between each iteration
                PreStep3_CacheDomeAzimuthRanges(state);

                var eligibleForNextPoint = eligiblePoints.Where(p => IsPointEligibleForBuild(p)).ToList();
                if (state.Options.MinimizeMeridianFlips) {
                    if (eligibleForNextPoint.Any(p => p.ExpectedDomeSideOfPier == nextPoint.ExpectedDomeSideOfPier)) {
                        eligibleForNextPoint = eligibleForNextPoint.Where(p => p.ExpectedDomeSideOfPier == nextPoint.ExpectedDomeSideOfPier).ToList();
                    } else if (eligibleForNextPoint.Any()) {
                        Logger.Info($"No more points on {nextPoint.ExpectedDomeSideOfPier} side of pier. Allowing flip to continue for the remaining points");
                    }
                }

                if (state.UseDome) {
                    var nextCandidates = eligibleForNextPoint.Where(p => IsPointVisibleThroughDome(p, 0.0d));
                    nextPoint = nextCandidates.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
                    if (nextPoint == null) {
                        // No points remaining visible through the slit. Widen the search to all eligible points on this side of the pier and slew the dome
                        nextPoint = eligibleForNextPoint.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
                        if (nextPoint != null) {
                            Logger.Info($"Next point not visible through dome. Dome slew required. Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}, MinDomeAz={nextPoint.MinDomeAzimuth:0.###}, MaxDomeAz={nextPoint.MaxDomeAzimuth:0.###}, CurrentDomeAz={domeMediator.GetInfo().Azimuth:0.###}");
                            _ = SlewDomeIfNecessary(state, eligibleForNextPoint, ct);
                        }
                    } else {
                        Logger.Info($"Next point still visible through dome. No dome slew required. Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}, MinDomeAz={nextPoint.MinDomeAzimuth:0.###}, MaxDomeAz={nextPoint.MaxDomeAzimuth:0.###}, CurrentDomeAz={domeMediator.GetInfo().Azimuth:0.###}");
                    }
                } else {
                    nextPoint = eligibleForNextPoint.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
                }

                PointNextUp?.Invoke(this, new PointNextUpEventArgs() { Point = nextPoint });
                if (nextPoint == null) {
                    Logger.Info("No points remaining");
                }
            }
        }

        private async Task<bool> SlewDomeIfNecessary(ModelBuilderState state, List<ModelPoint> sideOfPierPoints, CancellationToken ct) {
            if (state.DomeSlewTask != null) {
                throw new Exception("Dome slew requested while previous one is still in progress");
            }

            // The next dome slew destination is based on the next point in the ordering that is both still eligible for build, and doesn't have infinite dome azimuth range
            var nextAzimuthSlewPoint = sideOfPierPoints.Where(IsPointEligibleForBuild).Where(p => !double.IsNaN(p.MinDomeAzimuth)).OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
            if (nextAzimuthSlewPoint == null) {
                Logger.Info("No dome slew necessary. No eligible points remaining without an infinite dome azimuth range");
                return true;
            }

            double domeSlewAzimuth;
            if (state.Options.MinimizeDomeMovement) {
                domeSlewAzimuth = state.Options.WestToEastSorting ? nextAzimuthSlewPoint.MinDomeAzimuth : nextAzimuthSlewPoint.MaxDomeAzimuth;
            } else {
                domeSlewAzimuth = nextAzimuthSlewPoint.DomeAzimuth;
            }
            domeSlewAzimuth = AstroUtil.EuclidianModulus(domeSlewAzimuth, 360.0d);
            try {
                Logger.Info($"Next dome slew to {domeSlewAzimuth} based on point at Alt={nextAzimuthSlewPoint.Altitude:0.###}, Az={nextAzimuthSlewPoint.Azimuth:0.###}");
                state.DomeSlewTask = domeMediator.SlewToAzimuth(domeSlewAzimuth, ct);
                if (!await state.DomeSlewTask) {
                    Logger.Error("Dome slew failed");
                    Notification.ShowError("Dome Slew failed");
                    return false;
                }
                return true;
            } catch (OperationCanceledException) {
                Logger.Info("Dome slew cancelled");
                return false;
            } catch (Exception e) {
                Logger.Error("Dome slew failed", e);
                Notification.ShowError($"Dome Slew failed: {e.Message}");
                return false;
            } finally {
                state.DomeSlewTask = null;
            }
        }

        private static bool IsPointEligibleForBuild(ModelPoint point) {
            return point.ModelPointState == ModelPointStateEnum.Generated;
        }

        private bool IsPointVisibleThroughDome(ModelPoint point, double tolerance) {
            var domeAzimuth = domeMediator.GetInfo().Azimuth;
            var minDomeAzimuth = AstroUtil.EuclidianModulus(point.MinDomeAzimuth - tolerance, 360.0d);
            var maxDomeAzimuth = AstroUtil.EuclidianModulus(point.MaxDomeAzimuth + tolerance, 360.0d);
            if (maxDomeAzimuth < minDomeAzimuth) {
                return domeAzimuth >= minDomeAzimuth || domeAzimuth <= maxDomeAzimuth;
            } else {
                return domeAzimuth >= minDomeAzimuth && domeAzimuth <= maxDomeAzimuth;
            }
        }

        private async Task WaitForProcessing(List<Task<bool>> pendingTasks, CancellationToken ct, IProgress<ApplicationStatus> stepProgress) {
            try {
                Logger.Info($"Waiting for all {pendingTasks.Count} post-capture processing tasks to complete. {processingInProgressCount} remaining");
                var allPendingTasks = Task.WhenAll(pendingTasks);
                int inProgressTotal = processingInProgressCount;
                while (!allPendingTasks.IsCompleted) {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    stepProgress?.Report(new ApplicationStatus() {
                        Status = $"10u Remaining Solves",
                        ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                        Progress = Math.Max(inProgressTotal, inProgressTotal - processingInProgressCount),
                        MaxProgress = inProgressTotal
                    });
                }
            } finally {
                // Wait a bit before sending a final status update, since the Report is asynchronous and the previous one may still come in around the same time
                await Task.Delay(TimeSpan.FromSeconds(1));
                stepProgress?.Report(new ApplicationStatus() { });
            }
        }

        private void ValidateRequest(IList<ModelPoint> modelPoints) {
            foreach (var modelPoint in modelPoints) {
                if (modelPoint.Azimuth < 0 || modelPoint.Azimuth >= 360) {
                    throw new Exception($"Model point azimuth {modelPoint.Azimuth} must be within [0, 360)");
                }

                if (modelPoint.ModelPointState == ModelPointStateEnum.Generated) {
                    // Only validate points deemed not below the horizon
                    if (modelPoint.Altitude < 0 || modelPoint.Altitude > 90) {
                        throw new Exception($"Model point altitude {modelPoint.Altitude} must be within [0, 90]");
                    }
                }
            }
        }

        private void ReportOverallProgress(ModelBuilderState state, IProgress<ApplicationStatus> overallProgress) {
            var elapsedTime = DateTime.Now - state.IterationStartTime;
            var elapsedTimeSecondsRounded = TimeSpan.FromSeconds((int)elapsedTime.TotalSeconds);
            var completedPoints = state.PointsProcessed + state.FailedPoints;
            var totalPoints = state.ValidPoints.Count - state.PriorSuccessfulPointsProcessed;
            TimeSpan totalEstimatedTime;
            string elapsedProgressStatus;
            if (completedPoints >= 2) {
                var totalEstimatedTimeSeconds = elapsedTime.TotalSeconds * totalPoints / completedPoints;
                totalEstimatedTime = TimeSpan.FromSeconds((int)totalEstimatedTimeSeconds);
                elapsedProgressStatus = $"{elapsedTimeSecondsRounded:g} / {totalEstimatedTime:g}";
            } else {
                elapsedProgressStatus = $"{elapsedTimeSecondsRounded:g} / -";
            }

            overallProgress?.Report(new ApplicationStatus() {
                Status = $"Build Attempt",
                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                Progress = state.BuildAttempt,
                MaxProgress = state.Options.NumRetries + 1,
                Status2 = $"Elapsed {elapsedProgressStatus}",
                ProgressType2 = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                Progress2 = completedPoints,
                MaxProgress2 = totalPoints
            });
        }

        private bool AddModelPointToAlignmentSpec(ModelPoint point) {
            Logger.Info($"Adding alignment point to specification: {point}");
            int modelIndex = this.mountModelMediator.AddAlignmentStar(
                point.MountReportedRightAscension,
                point.MountReportedDeclination,
                point.MountReportedSideOfPier,
                point.PlateSolvedRightAscension,
                point.PlateSolvedDeclination,
                point.MountReportedLocalSiderealTime);
            if (modelIndex <= 0) {
                point.ModelPointState = ModelPointStateEnum.Failed;
                Logger.Error($"Failed to add {point} to alignment spec");
                return false;
            }

            point.ModelIndex = modelIndex;
            point.ModelPointState = ModelPointStateEnum.AddedToModel;
            return true;
        }

        private async Task<IExposureData> CaptureImage(ModelBuilderState state, ModelPoint point, IProgress<ApplicationStatus> stepProgress, CancellationToken ct) {
            point.MountReportedSideOfPier = mount.GetSideOfPier();
            point.MountReportedDeclination = mount.GetDeclination();
            point.MountReportedRightAscension = mount.GetRightAscension();
            point.CaptureTime = mount.GetUTCTime();
            point.MountReportedLocalSiderealTime = mount.GetLocalSiderealTime();
            var seq = new CaptureSequence(
                profileService.ActiveProfile.PlateSolveSettings.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                profileService.ActiveProfile.PlateSolveSettings.Filter,
                new BinningMode(profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.PlateSolveSettings.Binning),
                1
            );
            if (state.PlateSolveSubsample != null) {
                seq.SubSambleRectangle = state.PlateSolveSubsample;
                seq.EnableSubSample = true;
            }

            try {
                point.ModelPointState = ModelPointStateEnum.Exposing;
                var exposureData = await this.imagingMediator.CaptureImage(seq, ct, stepProgress);
                // Fire and forget to prepare image, which will put the latest captured image in the imaging tab view
                _ = Task.Run(async () => {
                    var imageData = await exposureData.ToImageData();
                    _ = this.imagingMediator.PrepareImage(imageData, new PrepareImageParameters(autoStretch: true, detectStars: false), ct);
                });
                return exposureData;
            } catch (Exception e) {
                Logger.Error($"Exception while capturing image for {point}", e);
                point.ModelPointState = ModelPointStateEnum.Failed;
                throw;
            }
        }

        private async Task<PlateSolveResult> SolveImage(ModelBuilderOptions options, IExposureData exposureData, CancellationToken ct) {
            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
            var blindSolver = options.AllowBlindSolves ? plateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings) : null;
            var solver = plateSolverFactory.GetImageSolver(plateSolver, blindSolver);
            var parameter = new PlateSolveParameter() {
                Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                Coordinates = telescopeMediator.GetCurrentPosition(),
                DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
            };

            // Plate solves are done concurrently, so do not show progress
            var imageData = await exposureData.ToImageData();
            return await solver.Solve(imageData, parameter, null, ct);
        }

        private async Task<bool> SolveAndCompleteProcessing(ModelBuilderState state, ModelPoint point, IExposureData exposureData, CancellationToken ct) {
            bool success = false;
            try {
                Interlocked.Increment(ref processingInProgressCount);

                ct.ThrowIfCancellationRequested();
                point.ModelPointState = ModelPointStateEnum.Processing;
                var plateSolveResult = await SolveImage(state.Options, exposureData, ct);
                if (plateSolveResult?.Success != true) {
                    Logger.Error($"Failed to plate solve model point: {point}");
                    return false;
                }
                ct.ThrowIfCancellationRequested();

                // Use the original mount-provided capture time to convert to JNow
                var captureTimeProvider = new ConstantDateTime(point.CaptureTime);
                var plateSolvedCoordinatesTimeAdjusted2 = new Coordinates(Angle.ByHours(plateSolveResult.Coordinates.RA), Angle.ByDegree(plateSolveResult.Coordinates.Dec), plateSolveResult.Coordinates.Epoch, captureTimeProvider);

                Logger.Info($"Unadjusted: {plateSolveResult.Coordinates}, Adjusted {plateSolvedCoordinatesTimeAdjusted2}");

                var plateSolvedCoordinatesTimeAdjusted = plateSolveResult.Coordinates;
                var plateSolvedCoordinates = plateSolvedCoordinatesTimeAdjusted.Transform(Epoch.JNOW);
                Logger.Info($"JNOW Unadjusted: {plateSolvedCoordinates}, Adjusted {plateSolvedCoordinatesTimeAdjusted2.Transform(Epoch.JNOW)}");

                var plateSolvedRightAscension = AstrometricTime.FromAngle(Angle.ByHours(plateSolvedCoordinates.RA));
                var plateSolvedDeclination = CoordinateAngle.FromAngle(Angle.ByDegree(plateSolvedCoordinates.Dec));
                point.PlateSolvedCoordinates = plateSolvedCoordinates;
                point.PlateSolvedRightAscension = plateSolvedRightAscension;
                point.PlateSolvedDeclination = plateSolvedDeclination;
                if (AddModelPointToAlignmentSpec(point)) {
                    success = true;
                }
            } catch (OperationCanceledException) {
            } catch (Exception e) {
                Logger.Error($"Exception during SolveAndCompleteProcessing for point: {point}", e);
            } finally {
                point.ModelPointState = success ? ModelPointStateEnum.AddedToModel : ModelPointStateEnum.Failed;
                Interlocked.Decrement(ref processingInProgressCount);
                state.ProcessingSemaphore.Release();
            }
            return success;
        }
    }
}