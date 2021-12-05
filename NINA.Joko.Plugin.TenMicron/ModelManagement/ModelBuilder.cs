#region "copyright"

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
        private readonly ICustomDateTime dateTime;
        private volatile int processingInProgressCount;

        public ModelBuilder(
            IProfileService profileService, IMountModelMediator mountModelMediator, IMount mount, ITelescopeMediator telescopeMediator, IDomeMediator domeMediator, ICameraMediator cameraMediator,
            IDomeSynchronization domeSynchronization, IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator,
            IWeatherDataMediator weatherDataMediator, ICustomDateTime dateTime) {
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
            this.dateTime = dateTime;
        }

        private class ModelBuilderState {

            public ModelBuilderState(ModelBuilderOptions options, List<ModelPoint> modelPoints, IMount mount, IDomeMediator domeMediator, IWeatherDataMediator weatherDataMediator) {
                this.Options = options;
                var maxConcurrent = options.MaxConcurrency > 0 ? options.MaxConcurrency : int.MaxValue;
                this.ProcessingSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
                this.ModelPoints = ImmutableList.ToImmutableList(modelPoints);
                this.ValidPoints = ImmutableList.ToImmutableList(modelPoints.Where(p => p.ModelPointState != ModelPointStateEnum.BelowHorizon));
                this.PendingTasks = new List<Task<bool>>();

                var domeInfo = domeMediator.GetInfo();
                this.UseDome = domeInfo?.Connected == true && domeInfo?.CanSetAzimuth == true;
                this.PointAzimuthComparer = GetPointComparer(this.UseDome, options);

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
            }

            public ModelBuilderOptions Options { get; private set; }
            public ImmutableList<ModelPoint> ModelPoints { get; private set; }
            public ImmutableList<ModelPoint> ValidPoints { get; private set; }
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
            public int PointsProcessed { get; set; }
            public int FailedPoints { get; set; }

            private static IComparer<ModelPoint> GetPointComparer(bool useDome, ModelBuilderOptions options) {
                if (useDome && options.MinimizeDomeMovement) {
                    return Comparer<ModelPoint>.Create(
                        (mp1, mp2) => {
                            if (!options.WestToEastSorting) {
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
                        (mp1, mp2) => !options.WestToEastSorting ? DOUBLE_COMPARER.Compare(mp1.Azimuth, mp2.Azimuth) : DOUBLE_COMPARER.Compare(mp2.Azimuth, mp1.Azimuth));
                }
            }
        }

        public async Task<LoadedAlignmentModel> Build(List<ModelPoint> modelPoints, ModelBuilderOptions options, CancellationToken ct = default, IProgress<ApplicationStatus> overallProgress = null, IProgress<ApplicationStatus> stepProgress = null) {
            ct.ThrowIfCancellationRequested();
            PreFlightChecks(modelPoints);

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

            var state = new ModelBuilderState(options, modelPoints, mount, domeMediator, weatherDataMediator);
            var innerCts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, innerCts.Token);
            try {
                return await DoBuild(state, linkedCts.Token, overallProgress, stepProgress);
            } finally {
                overallProgress?.Report(new ApplicationStatus() {
                    Status = "",
                    Status2 = ""
                });
                stepProgress?.Report(new ApplicationStatus() { });
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
                state.ProcessingSemaphore?.Dispose();
                // Make sure any remaining tasks are cancelled, just in case an exception left some remaining work in progress
                innerCts.Cancel();
            }
        }

        private void PreFlightChecks(List<ModelPoint> modelPoints) {
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

        private async Task<LoadedAlignmentModel> DoBuild(ModelBuilderState state, CancellationToken ct, IProgress<ApplicationStatus> overallProgress, IProgress<ApplicationStatus> stepProgress) {
            ct.ThrowIfCancellationRequested();
            var validPoints = state.ValidPoints;
            var options = state.Options;

            // Pre-Step 1: Clear state for all points except those below the horizon
            PreStep1_ClearState(state);
            processingInProgressCount = 0;

            // Pre-Step 2: Calculate refraction-correction adjusted RA/DEC for selected Alt/Az points
            PreStep2_CacheCelestialCoordinates(state, ct);

            // Pre-Step 3: If a dome is connected, pre-compute all dome ranges since we're using a fixed Alt/Az for each point
            PreStep3_CacheDomeAzimuthRanges(state);

            int retryCount = -1;
            LoadedAlignmentModel builtModel = null;
            while (retryCount++ < options.NumRetries) {
                state.FailedPoints = 0;
                state.BuildAttempt = retryCount + 1;

                Logger.Info($"Starting model build iteration {retryCount + 1}");
                ReportOverallProgress(state, overallProgress);

                // Step 1: Clear alignment model
                Logger.Info("Deleting current alignment model");
                this.mountModelMediator.DeleteAlignment();

                // Step 2: Start new alignment model
                Logger.Info("Starting new alignment spec");
                if (!this.mountModelMediator.StartNewAlignmentSpec()) {
                    throw new ModelBuildException("Failed to start new alignment spec");
                }

                // Step 3: Add all successful points, which is applicable for retries
                {
                    var existingSuccessfulPoints = validPoints.Where(p => p.ModelPointState == ModelPointStateEnum.AddedToModel).ToList();
                    if (existingSuccessfulPoints.Count > 0) {
                        Logger.Info($"Adding {existingSuccessfulPoints.Count} previously successful points to the new alignment spec");
                        foreach (var point in existingSuccessfulPoints) {
                            if (!AddModelPointToAlignmentSpec(point)) {
                                Logger.Error($"Failed to add point {point} during retry. Changing to failed state");
                                ++state.FailedPoints;
                                point.ModelPointState = ModelPointStateEnum.Failed;
                            }
                            ++state.PointsProcessed;
                            ReportOverallProgress(state, overallProgress);
                        }
                    }
                }

                // Step 4: Process points based on ordering. If dome is involved, it is the point with the least minimum azimuth range or the largest maximum azimuth range, based on E/W ordering and whether MinimizeDomeMovement is enabled
                await ProcessPoints(state, ct, overallProgress, stepProgress);
                ct.ThrowIfCancellationRequested();

                // Step 5: Wait for remaining pending processing tasks
                await WaitForProcessing(state.PendingTasks, ct, stepProgress);
                ct.ThrowIfCancellationRequested();

                var numPendingFailures = state.PendingTasks.Select(pt => pt.Result).Count(x => !x);
                Logger.Info($"{numPendingFailures} failures during post-capture processing");
                state.FailedPoints += numPendingFailures;

                var completedPoints = validPoints.Count - state.FailedPoints;
                if (completedPoints > 2) {
                    Logger.Info("Completing alignment spec");
                    if (!mountModelMediator.FinishAlignmentSpec()) {
                        Logger.Error("Failed to complete alignment spec. Aborting");
                        return null;
                    }

                    builtModel = await mountModelMediator.GetLoadedAlignmentModel(ct);
                    ct.ThrowIfCancellationRequested();
                    var modelAlignmentStars = builtModel.AlignmentStars.ToArray();
                    foreach (var point in validPoints) {
                        if (point.ModelPointState == ModelPointStateEnum.AddedToModel) {
                            var successfulPoint = false;
                            if (point.ModelIndex > 0 && point.ModelIndex <= modelAlignmentStars.Length) {
                                point.RMSError = modelAlignmentStars[point.ModelIndex - 1].ErrorArcsec;
                                // TODO: Add check for max RMS error to fail the star
                                successfulPoint = true;
                            } else {
                                Logger.Error($"Point {point} has invalid model index {point.ModelIndex}. There are {modelAlignmentStars.Length} alignment stars in the model");
                            }

                            if (!successfulPoint) {
                                point.ModelPointState = ModelPointStateEnum.Failed;
                                ++state.FailedPoints;
                                --completedPoints;
                            }
                        }
                    }
                } else {
                    Logger.Error("Not enough successful points to complete alignment spec");
                }

                if (state.FailedPoints == 0) {
                    Logger.Info($"No failed points remaining after build iteration {state.BuildAttempt}");
                    break;
                } else {
                    Logger.Info($"{state.FailedPoints} failed points during model build iteration {state.BuildAttempt}. {options.NumRetries - state.BuildAttempt + 1} retries remaining");
                }
            }

            return builtModel;
        }

        private void PreStep1_ClearState(ModelBuilderState state) {
            foreach (var point in state.ValidPoints) {
                point.ModelIndex = -1;
                point.Coordinates = null;
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

        private void PreStep2_CacheCelestialCoordinates(ModelBuilderState state, CancellationToken ct) {
            Logger.Info($"Refraction correction={state.RefractionCorrectionEnabled}. Using pressure={state.PressurehPa}, temperature={state.Temperature}, relative humidity={state.Humidity}, wavelength={state.Wavelength}");
            Logger.Info("Caching celestial coordinates for proximity sorting");
            foreach (var point in state.ValidPoints) {
                ct.ThrowIfCancellationRequested();
                point.Coordinates = point.ToCelestial(pressurehPa: state.PressurehPa, tempCelcius: state.Temperature, relativeHumidity: state.Humidity, wavelength: state.Wavelength);
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
                var celestialCoordinates = modelPoint.ToTopocentric().Transform(Epoch.JNOW);
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

                Logger.Info($"Point at Alt={modelPoint.Altitude}, Az={modelPoint.Azimuth} requires dome azimuth between [{AstroUtil.EuclidianModulus(minAzimuth.Degree, 360.0d)}, {AstroUtil.EuclidianModulus(maxAzimuth.Degree, 360.0d)}]");
                modelPoint.MinDomeAzimuth = minAzimuth.Degree;
                modelPoint.MaxDomeAzimuth = maxAzimuth.Degree;
                modelPoint.DomeAzimuth = domeAzimuth.Degree;
            }
        }

        private async Task ProcessPoints(
            ModelBuilderState state,
            CancellationToken ct,
            IProgress<ApplicationStatus> overallProgress,
            IProgress<ApplicationStatus> stepProgress) {
            var sideOfPierPoints = state.ValidPoints.Where(IsPointEligibleForBuild).ToList();
            var nextPoint = sideOfPierPoints.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
            Logger.Info($"Processing {sideOfPierPoints.Count} points. First point Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}, MinDomeAz={nextPoint.MinDomeAzimuth:0.###}, MaxDomeAz={nextPoint.MaxDomeAzimuth:0.###}");
            if (state.UseDome) {
                _ = SlewDomeIfNecessary(state, sideOfPierPoints, ct);
            }

            while (nextPoint != null) {
                ct.ThrowIfCancellationRequested();
                nextPoint.ModelPointState = ModelPointStateEnum.UpNext;

                bool success = false;
                try {
                    // TODO: Use AltAz slew on mount
                    var nextPointCoordinates = nextPoint.ToCelestial(pressurehPa: state.PressurehPa, tempCelcius: state.Temperature, relativeHumidity: state.Humidity, wavelength: state.Wavelength);
                    Logger.Info($"Slewing to {nextPointCoordinates} for point at Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}");
                    // TODO: Move this to a utility method
                    if (!await this.telescopeMediator.SlewToCoordinatesAsync(nextPointCoordinates, ct)) {
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
                            }

                            // Successfully slewed to point. Take an exposure
                            var exposureData = await CaptureImage(nextPoint, stepProgress, ct);
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
                ReportOverallProgress(state, overallProgress);
                if (!success) {
                    nextPoint.ModelPointState = ModelPointStateEnum.Failed;
                    ++state.FailedPoints;
                }

                // Using a simple azimuth-based ordering for now.
                // TODO: Sequence slews based on cartesian distance?
                // TODO: Add Stop vs Cancel button
                // TODO: Add execution timer, including remaining estimate
                // TODO: Add line for "next point dome azimuth"
                // TODO: Split download from exposure in NINA core
                // TODO: Update plugin description to represent what is supported
                // TODO: Disable/Re-enable dome following
                // TODO: Slew to AltAz instead of using transformations
                // TODO: Add option to save failed points and plate solve image
                if (state.UseDome) {
                    var nextCandidates = sideOfPierPoints.Where(p => IsPointEligibleForBuild(p) && IsPointVisibleThroughDome(p));
                    nextPoint = nextCandidates.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
                    if (nextPoint == null) {
                        // No points remaining visible through the slit. Widen the search to all eligible points on this side of the pier and slew the dome
                        nextCandidates = sideOfPierPoints.Where(IsPointEligibleForBuild);
                        nextPoint = nextCandidates.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
                        if (nextPoint != null) {
                            Logger.Info($"Next point not visible through dome. Dome slew required. Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}, MinDomeAz={nextPoint.MinDomeAzimuth:0.###}, MaxDomeAz={nextPoint.MaxDomeAzimuth:0.###}, CurrentDomeAz={domeMediator.GetInfo().Azimuth:0.###}");
                            _ = SlewDomeIfNecessary(state, sideOfPierPoints, ct);
                        }
                    } else {
                        Logger.Info($"Next point still visible through dome. No dome slew required. Alt={nextPoint.Altitude:0.###}, Az={nextPoint.Azimuth:0.###}, MinDomeAz={nextPoint.MinDomeAzimuth:0.###}, MaxDomeAz={nextPoint.MaxDomeAzimuth:0.###}, CurrentDomeAz={domeMediator.GetInfo().Azimuth:0.###}");
                    }
                } else {
                    var nextCandidates = sideOfPierPoints.Where(IsPointEligibleForBuild);
                    nextPoint = nextCandidates.OrderBy(p => p, state.PointAzimuthComparer).FirstOrDefault();
                }

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

        private bool IsPointVisibleThroughDome(ModelPoint point) {
            var domeAzimuth = domeMediator.GetInfo().Azimuth;
            var minDomeAzimuth = AstroUtil.EuclidianModulus(point.MinDomeAzimuth, 360.0d);
            var maxDomeAzimuth = AstroUtil.EuclidianModulus(point.MaxDomeAzimuth, 360.0d);
            if (maxDomeAzimuth < minDomeAzimuth) {
                return domeAzimuth > minDomeAzimuth || domeAzimuth < maxDomeAzimuth;
            } else {
                return domeAzimuth > minDomeAzimuth && domeAzimuth < maxDomeAzimuth;
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
                stepProgress?.Report(new ApplicationStatus() { });
            }
        }

        private void ValidateRequest(List<ModelPoint> modelPoints) {
            foreach (var modelPoint in modelPoints) {
                if (modelPoint.Azimuth < 0 || modelPoint.Azimuth >= 360) {
                    throw new Exception($"Model point azimuth {modelPoint.Azimuth} must be within [0, 360)");
                }
                if (modelPoint.Altitude < 0 || modelPoint.Altitude > 90) {
                    throw new Exception($"Model point altitude {modelPoint.Altitude} must be within [0, 90]");
                }
            }
        }

        private void ReportOverallProgress(ModelBuilderState state, IProgress<ApplicationStatus> overallProgress) {
            overallProgress?.Report(new ApplicationStatus() {
                Status = $"Build Attempt",
                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                Progress = state.BuildAttempt,
                MaxProgress = state.Options.NumRetries + 1,
                Status2 = $"Stars",
                ProgressType2 = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
                Progress2 = state.PointsProcessed,
                MaxProgress2 = state.ValidPoints.Count
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

        private async Task<IExposureData> CaptureImage(ModelPoint point, IProgress<ApplicationStatus> stepProgress, CancellationToken ct) {
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
            point.ModelPointState = ModelPointStateEnum.Exposing;
            return await this.imagingMediator.CaptureImage(seq, ct, stepProgress);
        }

        private async Task<PlateSolveResult> SolveImage(ModelBuilderOptions options, IExposureData exposureData, CancellationToken ct) {
            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
            var blindSolver = options.AllowBlindSolves ? plateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings) : null;
            var solver = plateSolverFactory.GetCaptureSolver(plateSolver, blindSolver, imagingMediator, filterWheelMediator);
            var parameter = new CaptureSolverParameter() {
                Attempts = profileService.ActiveProfile.PlateSolveSettings.NumberOfAttempts,
                Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                Coordinates = telescopeMediator.GetCurrentPosition(),
                DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                ReattemptDelay = TimeSpan.FromMinutes(profileService.ActiveProfile.PlateSolveSettings.ReattemptDelay),
                Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius
            };

            // Plate solves are done concurrently, so do not show progress
            var imageData = await exposureData.ToImageData();
            return await solver.ImageSolver.Solve(imageData, parameter, null, ct);
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