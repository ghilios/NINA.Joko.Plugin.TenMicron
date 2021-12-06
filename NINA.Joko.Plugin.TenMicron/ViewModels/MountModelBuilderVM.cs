#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyDome;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.Joko.Plugin.TenMicron.ViewModels {

    [Export(typeof(IDockableVM))]
    public class MountModelBuilderVM : DockableVM, ITelescopeConsumer, IMountConsumer, IDomeConsumer {
        private static readonly CustomHorizon EMPTY_HORIZON = GetEmptyHorizon();
        private readonly IMountMediator mountMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IModelPointGenerator modelPointGenerator;
        private readonly IModelBuilder modelBuilder;

        private readonly ITenMicronOptions modelBuilderOptions;
        private IProgress<ApplicationStatus> progress;
        private IProgress<ApplicationStatus> stepProgress;
        private bool disposed = false;
        private CancellationTokenSource disconnectCts;

        [ImportingConstructor]
        public MountModelBuilderVM(IProfileService profileService, IApplicationStatusMediator applicationStatusMediator, ITelescopeMediator telescopeMediator, IDomeMediator domeMediator) :
            this(profileService,
                TenMicronPlugin.TenMicronOptions,
                telescopeMediator,
                domeMediator,
                applicationStatusMediator,
                TenMicronPlugin.MountMediator,
                TenMicronPlugin.ModelPointGenerator,
                TenMicronPlugin.ModelBuilder) {
        }

        public MountModelBuilderVM(
            IProfileService profileService,
            ITenMicronOptions modelBuilderOptions,
            ITelescopeMediator telescopeMediator,
            IDomeMediator domeMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IMountMediator mountMediator,
            IModelPointGenerator modelPointGenerator,
            IModelBuilder modelBuilder) : base(profileService) {
            this.Title = "10u Model Builder";
            this.modelBuilderOptions = modelBuilderOptions;
            this.applicationStatusMediator = applicationStatusMediator;
            this.mountMediator = mountMediator;
            this.telescopeMediator = telescopeMediator;
            this.domeMediator = domeMediator;
            this.modelPointGenerator = modelPointGenerator;
            this.modelBuilder = modelBuilder;
            this.modelBuilder.PointNextUp += ModelBuilder_PointNextUp;
            this.modelBuilderOptions.PropertyChanged += ModelBuilderOptions_PropertyChanged;

            this.disconnectCts = new CancellationTokenSource();

            this.telescopeMediator.RegisterConsumer(this);
            this.domeMediator.RegisterConsumer(this);
            this.mountMediator.RegisterConsumer(this);

            this.profileService.ProfileChanged += ProfileService_ProfileChanged;
            this.profileService.ActiveProfile.AstrometrySettings.PropertyChanged += AstrometrySettings_PropertyChanged;
            this.profileService.ActiveProfile.DomeSettings.PropertyChanged += DomeSettings_PropertyChanged;
            this.LoadHorizon();

            this.GeneratePointsCommand = new AsyncCommand<bool>(GeneratePoints);
            this.ClearPointsCommand = new AsyncCommand<bool>(ClearPoints);
            this.BuildCommand = new AsyncCommand<bool>(BuildModel);
            this.CancelBuildCommand = new AsyncCommand<bool>(CancelBuildModel);
            this.StopBuildCommand = new AsyncCommand<bool>(StopBuildModel);
        }

        private void ModelBuilderOptions_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(modelBuilderOptions.ShowRemovedPoints)) {
                if (!this.modelBuilderOptions.ShowRemovedPoints) {
                    var localModelPoints = this.ModelPoints.Where(mp => mp.ModelPointState == ModelPointStateEnum.Generated);
                    this.DisplayModelPoints = new AsyncObservableCollection<ModelPoint>(localModelPoints);
                } else {
                    this.DisplayModelPoints = new AsyncObservableCollection<ModelPoint>(this.ModelPoints);
                }
            }
        }

        private void ModelBuilder_PointNextUp(object sender, PointNextUpEventArgs e) {
            if (e.Point == null || double.IsNaN(e.Point.DomeAzimuth)) {
                this.NextUpDomeAzimuthPosition = new DataPoint();
                this.ShowNextUpDomeAzimuthPosition = false;
            } else {
                this.NextUpDomeAzimuthPosition = new DataPoint(e.Point.DomeAzimuth, e.Point.Altitude);
                this.ShowNextUpDomeAzimuthPosition = true;
            }
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            this.profileService.ActiveProfile.AstrometrySettings.PropertyChanged += AstrometrySettings_PropertyChanged;
            this.profileService.ActiveProfile.DomeSettings.PropertyChanged += DomeSettings_PropertyChanged;
            this.LoadHorizon();
        }

        private void DomeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(this.profileService.ActiveProfile.DomeSettings.AzimuthTolerance_degrees)) {
                _ = CalculateDomeShutterOpening(disconnectCts.Token);
            }
        }

        private void AstrometrySettings_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(this.profileService.ActiveProfile.AstrometrySettings.Horizon)) {
                this.LoadHorizon();
            }
        }

        private void LoadHorizon() {
            this.CustomHorizon = this.profileService.ActiveProfile.AstrometrySettings.Horizon;
            if (this.CustomHorizon == null) {
                this.HorizonDataPoints.Clear();
            } else {
                var dataPoints = new List<DataPoint>();
                for (double azimuth = 0.0; azimuth <= 360.0; azimuth += 1.0) {
                    var horizonAltitude = CustomHorizon.GetAltitude(azimuth);
                    dataPoints.Add(new DataPoint(azimuth, horizonAltitude));
                }
                this.HorizonDataPoints = new AsyncObservableCollection<DataPoint>(dataPoints);
            }
        }

        public override bool IsTool { get; } = true;

        public void Dispose() {
            if (!this.disposed) {
                this.telescopeMediator.RemoveConsumer(this);
                this.mountMediator.RemoveConsumer(this);
                this.disposed = true;
            }
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            this.TelescopeInfo = deviceInfo;
        }

        private static readonly Angle DomeShutterOpeningRefreshTolerance = Angle.ByDegree(1.0);

        public void UpdateDeviceInfo(DomeInfo deviceInfo) {
            this.DomeInfo = deviceInfo;
            this.DomeControlEnabled = this.DomeInfo.Connected && this.DomeInfo.CanSetAzimuth;
            if (this.DomeControlEnabled) {
                var currentAzimuth = Angle.ByDegree(this.DomeInfo.Azimuth);
                if (domeShutterAzimuthForOpening == null || !currentAzimuth.Equals(domeShutterAzimuthForOpening, DomeShutterOpeningRefreshTolerance)) {
                    // Asynchronously update the dome shutter opening after the dome azimuth changes beyond the threshold it was last calculated for
                    _ = CalculateDomeShutterOpening(disconnectCts.Token);
                    domeShutterAzimuthForOpening = currentAzimuth;
                }
            }
        }

        public void UpdateDeviceInfo(MountInfo deviceInfo) {
            this.MountInfo = deviceInfo;
            if (this.MountInfo.Connected) {
                Connect();
            } else {
                Disconnect();
            }
        }

        private MountInfo mountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();

        public MountInfo MountInfo {
            get => mountInfo;
            private set {
                mountInfo = value;
                RaisePropertyChanged();
            }
        }

        private TelescopeInfo telescopeInfo = DeviceInfo.CreateDefaultInstance<TelescopeInfo>();

        public TelescopeInfo TelescopeInfo {
            get => telescopeInfo;
            private set {
                telescopeInfo = value;
                RaisePropertyChanged();
                if (Connected) {
                    ScopePosition = new DataPoint(telescopeInfo.Azimuth, telescopeInfo.Altitude);
                }
            }
        }

        private DomeInfo domeInfo = DeviceInfo.CreateDefaultInstance<DomeInfo>();

        public DomeInfo DomeInfo {
            get => domeInfo;
            private set {
                domeInfo = value;
                RaisePropertyChanged();
            }
        }

        private bool connected;

        public bool Connected {
            get => connected;
            private set {
                if (connected != value) {
                    connected = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void Connect() {
            if (Connected) {
                return;
            }

            if (this.progress == null) {
                this.progress = new Progress<ApplicationStatus>(p => {
                    p.Source = this.Title;
                    this.applicationStatusMediator.StatusUpdate(p);
                });
            }
            if (this.stepProgress == null) {
                this.stepProgress = new Progress<ApplicationStatus>(p => {
                    p.Source = "10u Build Step";
                    this.applicationStatusMediator.StatusUpdate(p);
                });
            }

            this.disconnectCts?.Cancel();
            this.disconnectCts = new CancellationTokenSource();
            this.domeShutterAzimuthForOpening = null;
            this.DomeShutterOpeningDataPoints.Clear();
            this.DomeShutterOpeningDataPoints2.Clear();
            this.BuildInProgress = false;
            Connected = true;
        }

        private void Disconnect() {
            if (!Connected) {
                return;
            }

            this.disconnectCts?.Cancel();
            Connected = false;
        }

        private Task<bool> GeneratePoints(object o) {
            try {
                if (this.ModelPointGenerationType == ModelPointGenerationTypeEnum.GoldenSpiral) {
                    return Task.FromResult(GenerateGoldenSpiral(this.GoldenSpiralStarCount));
                } else if (this.ModelPointGenerationType == ModelPointGenerationTypeEnum.SiderealPath) {
                    return Task.FromResult(GenerateSiderealPath());
                } else {
                    throw new ArgumentException($"Unexpected Model Point Generation Type {this.ModelPointGenerationType}");
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to generate points. {e.Message}");
                Logger.Error($"Failed to generate points", e);
                return Task.FromResult(false);
            }
        }

        private Task<bool> ClearPoints(object o) {
            this.ModelPoints.Clear();
            this.DisplayModelPoints.Clear();
            return Task.FromResult(true);
        }

        private bool GenerateGoldenSpiral(int goldenSpiralStarCount) {
            var localModelPoints = this.modelPointGenerator.GenerateGoldenSpiral(goldenSpiralStarCount, this.CustomHorizon);
            this.ModelPoints = ImmutableList.ToImmutableList(localModelPoints);
            if (!this.modelBuilderOptions.ShowRemovedPoints) {
                localModelPoints = localModelPoints.Where(mp => mp.ModelPointState == ModelPointStateEnum.Generated).ToList();
            }
            this.DisplayModelPoints = new AsyncObservableCollection<ModelPoint>(localModelPoints);
            return true;
        }

        private bool GenerateSiderealPath() {
            Notification.ShowError("Sidereal Path not yet implemented");
            return false;
        }

        private CancellationTokenSource modelBuildCts;
        private CancellationTokenSource modelBuildStopCts;
        private Task<LoadedAlignmentModel> modelBuildTask;

        private async Task<bool> BuildModel(object o) {
            try {
                if (modelBuildCts != null) {
                    throw new Exception("Model build already in progress");
                }

                BuildInProgress = true;
                modelBuildCts = new CancellationTokenSource();
                modelBuildStopCts = new CancellationTokenSource();
                var options = new ModelBuilderOptions() {
                    WestToEastSorting = WestToEastSorting,
                    NumRetries = BuilderNumRetries,
                    MaxPointRMS = BuilderNumRetries > 0 ? MaxPointRMS : double.PositiveInfinity,
                    MinimizeDomeMovement = MinimizeDomeMovementEnabled,
                    SyncFirstPoint = modelBuilderOptions.SyncFirstPoint,
                    AllowBlindSolves = modelBuilderOptions.AllowBlindSolves,
                    MaxConcurrency = modelBuilderOptions.MaxConcurrency,
                    DomeShutterWidth_mm = DomeShutterWidth_mm,
                    MaxFailedPoints = MaxFailedPoints
                };
                var modelPoints = ModelPoints.ToList();
                modelBuildTask = modelBuilder.Build(modelPoints, options, modelBuildCts.Token, modelBuildStopCts.Token, progress, stepProgress);
                var builtModel = await modelBuildTask;
                modelBuildTask = null;
                modelBuildCts = null;
                modelBuildStopCts = null;
                if (builtModel == null) {
                    Notification.ShowError($"Failed to build 10u model");
                    return false;
                } else {
                    Notification.ShowInformation($"10u model build completed. {builtModel.AlignmentStarCount} stars, RMS error {builtModel.RMSError:0.##} arcsec");
                }
                return true;
            } catch (OperationCanceledException) {
                Notification.ShowInformation("Model build cancelled");
                Logger.Info("Model build cancelled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Failed to build model. {e.Message}");
                Logger.Error($"Failed to build model", e);
                return false;
            } finally {
                modelBuildCts?.Cancel();
                modelBuildCts = null;
                modelBuildStopCts = null;
                BuildInProgress = false;
            }
        }

        private async Task<bool> CancelBuildModel(object o) {
            try {
                modelBuildCts?.Cancel();
                var localModelBuildTask = modelBuildTask;
                if (localModelBuildTask != null) {
                    await localModelBuildTask;
                }
                return true;
            } catch (Exception) {
                return false;
            }
        }

        private async Task<bool> StopBuildModel(object o) {
            try {
                modelBuildStopCts?.Cancel();
                var localModelBuildTask = modelBuildTask;
                if (localModelBuildTask != null) {
                    await localModelBuildTask;
                }
                return true;
            } catch (Exception) {
                return false;
            }
        }

        private static CustomHorizon GetEmptyHorizon() {
            var horizonDefinition = $"0 0" + Environment.NewLine + "360 0";
            using (var sr = new StringReader(horizonDefinition)) {
                return CustomHorizon.FromReader_Standard(sr);
            }
        }

        private Task calculateDomeShutterOpeningTask;

        private async Task CalculateDomeShutterOpening(CancellationToken ct) {
            if (calculateDomeShutterOpeningTask != null) {
                await calculateDomeShutterOpeningTask;
                return;
            }

            calculateDomeShutterOpeningTask = Task.Run(() => {
                var azimuth = DomeInfo.Azimuth;
                if (DomeShutterWidth_mm <= 0.0) {
                    CalculateFixedThresholdDomeShutterOpening(azimuth, ct);
                } else {
                    CalculateAzimuthAwareDomeShutterOpening(azimuth, ct);
                }
            }, ct);

            try {
                await calculateDomeShutterOpeningTask;
            } catch (Exception e) {
                Logger.Error("Failed to calculate dome shutter opening", e);
            } finally {
                calculateDomeShutterOpeningTask = null;
            }
        }

        private void CalculateFixedThresholdDomeShutterOpening(double azimuth, CancellationToken ct) {
            var azimuthTolerance = profileService.ActiveProfile.DomeSettings.AzimuthTolerance_degrees;
            var dataPoints = new List<DomeShutterOpeningDataPoint>();
            var dataPoints2 = new List<DomeShutterOpeningDataPoint>();

            if (azimuth - azimuthTolerance < 0.0 || azimuth + azimuthTolerance > 360.0) {
                dataPoints.Add(new DomeShutterOpeningDataPoint() {
                    Azimuth = 0,
                    MinAltitude = 0.0,
                    MaxAltitude = 90.0
                });
                dataPoints.Add(new DomeShutterOpeningDataPoint() {
                    Azimuth = (azimuth + azimuthTolerance) % 360.0,
                    MinAltitude = 0.0,
                    MaxAltitude = 90.0
                });
                dataPoints2.Add(new DomeShutterOpeningDataPoint() {
                    Azimuth = (azimuth - azimuthTolerance + 360.0) % 360.0,
                    MinAltitude = 0.0,
                    MaxAltitude = 90.0
                });
                dataPoints2.Add(new DomeShutterOpeningDataPoint() {
                    Azimuth = 360.0,
                    MinAltitude = 0.0,
                    MaxAltitude = 90.0
                });
            } else {
                dataPoints.Add(new DomeShutterOpeningDataPoint() {
                    Azimuth = azimuth - azimuthTolerance,
                    MinAltitude = 0.0,
                    MaxAltitude = 90.0
                });
                dataPoints.Add(new DomeShutterOpeningDataPoint() {
                    Azimuth = azimuth + azimuthTolerance,
                    MinAltitude = 0.0,
                    MaxAltitude = 90.0
                });
            }
            ct.ThrowIfCancellationRequested();
            this.DomeShutterOpeningDataPoints = new AsyncObservableCollection<DomeShutterOpeningDataPoint>(dataPoints);
            this.DomeShutterOpeningDataPoints2 = new AsyncObservableCollection<DomeShutterOpeningDataPoint>(dataPoints2);
        }

        private void CalculateAzimuthAwareDomeShutterOpening(double azimuth, CancellationToken ct) {
            var azimuthTolerance = profileService.ActiveProfile.DomeSettings.AzimuthTolerance_degrees;
            var dataPoints1_1 = new List<DomeShutterOpeningDataPoint>();
            var dataPoints1_2 = new List<DomeShutterOpeningDataPoint>();
            var dataPoints2_1 = new List<DomeShutterOpeningDataPoint>();
            var dataPoints2_2 = new List<DomeShutterOpeningDataPoint>();
            var azimuthAngle = Angle.ByDegree(azimuth);
            var domeRadius = this.profileService.ActiveProfile.DomeSettings.DomeRadius_mm;
            if (domeRadius <= 0) {
                throw new ArgumentException("Dome Radius is not set in Dome Options");
            }

            const double altitudeDelta = 3.0d;
            for (double altitude = 0.0; altitude <= 90.0; altitude += altitudeDelta) {
                ct.ThrowIfCancellationRequested();
                var altitudeAngle = Angle.ByDegree(altitude);
                (var leftAzimuthBoundary, var rightAzimuthBoundary) = DomeUtility.CalculateDomeAzimuthRange(altitudeAngle: altitudeAngle, azimuthAngle: azimuthAngle, domeRadius: domeRadius, domeShutterWidthMm: DomeShutterWidth_mm);
                if (leftAzimuthBoundary.Degree < 0.0) {
                    var addDegrees = AstroUtil.EuclidianModulus(leftAzimuthBoundary.Degree, 360.0d) - leftAzimuthBoundary.Degree;
                    leftAzimuthBoundary += Angle.ByDegree(addDegrees);
                    rightAzimuthBoundary += Angle.ByDegree(addDegrees);
                }

                if (rightAzimuthBoundary.Degree < 360.0) {
                    dataPoints1_1.Add(new DomeShutterOpeningDataPoint() {
                        Azimuth = leftAzimuthBoundary.Degree,
                        MinAltitude = altitude,
                        MaxAltitude = 90.0
                    });
                    dataPoints1_2.Add(new DomeShutterOpeningDataPoint() {
                        Azimuth = rightAzimuthBoundary.Degree,
                        MinAltitude = altitude,
                        MaxAltitude = 90.0
                    });
                } else {
                    if (azimuth > 180.0d) {
                        dataPoints1_1.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = leftAzimuthBoundary.Degree,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                        dataPoints1_2.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = 359.9d,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                        dataPoints2_1.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = 0.0,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                        dataPoints2_2.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = rightAzimuthBoundary.Degree - 360.0,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                    } else {
                        dataPoints2_1.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = leftAzimuthBoundary.Degree,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                        dataPoints2_2.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = 359.9d,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                        dataPoints1_1.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = 0.0,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                        dataPoints1_2.Add(new DomeShutterOpeningDataPoint() {
                            Azimuth = rightAzimuthBoundary.Degree - 360.0,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        });
                    }
                }
            }

            ct.ThrowIfCancellationRequested();
            dataPoints1_1.Reverse();
            dataPoints2_1.Reverse();
            this.DomeShutterOpeningDataPoints = new AsyncObservableCollection<DomeShutterOpeningDataPoint>(dataPoints1_1.Concat(dataPoints1_2));
            this.DomeShutterOpeningDataPoints2 = new AsyncObservableCollection<DomeShutterOpeningDataPoint>(dataPoints2_1.Concat(dataPoints2_2));
        }

        /*
         * This block was used to provide dome coverage charts that allowed for infinite range. This should be brought back when the zenith can properly be accounted for
        private void CalculateAzimuthAwareDomeShutterOpening(double azimuth, CancellationToken ct) {
            var dataPoints = new List<DomeShutterOpeningDataPoint>();
            var azimuthAngle = Angle.ByDegree(azimuth);
            var domeRadius = this.profileService.ActiveProfile.DomeSettings.DomeRadius_mm;
            if (domeRadius <= 0) {
                throw new ArgumentException("Dome Radius is not set in Dome Options");
            }

            var fullCoverageReached = false;
            DomeShutterOpeningDataPoint leftLimit = null;
            DomeShutterOpeningDataPoint rightLimit = null;
            const double altitudeDelta = 3.0d;
            for (double altitude = 0.0; altitude <= 90.0; altitude += altitudeDelta) {
                var altitudeAngle = Angle.ByDegree(altitude);
                (var leftAzimuthBoundary, var rightAzimuthBoundary) = DomeUtility.CalculateDomeAzimuthRange(altitudeAngle: altitudeAngle, azimuthAngle: azimuthAngle, domeRadius: domeRadius, domeShutterWidthMm: DomeShutterWidth_mm);
                var apertureAngleThresholdDegree = (azimuthAngle - leftAzimuthBoundary).Degree;

                if (leftAzimuthBoundary.Degree < 0.0 || rightAzimuthBoundary.Degree > 360.0) {
                    double boundaryAltitude;
                    // Interpolate since the target azimuth is beyond the circle boundary
                    if (leftAzimuthBoundary.Degree < 0.0) {
                        boundaryAltitude = altitude - altitudeDelta * (1.0d - azimuthAngle.Degree / apertureAngleThresholdDegree);
                    } else {
                        boundaryAltitude = altitude - altitudeDelta * (1.0d - (360.0d - azimuthAngle.Degree) / apertureAngleThresholdDegree);
                    }

                    if (leftLimit == null || leftLimit.MinAltitude > boundaryAltitude) {
                        leftLimit = new DomeShutterOpeningDataPoint() {
                            Azimuth = 0.0,
                            MinAltitude = boundaryAltitude,
                            MaxAltitude = 90.0
                        };
                    }
                    if (rightLimit == null || rightLimit.MinAltitude > boundaryAltitude) {
                        rightLimit = new DomeShutterOpeningDataPoint() {
                            Azimuth = 360.0,
                            MinAltitude = boundaryAltitude,
                            MaxAltitude = 90.0
                        };
                    }
                }
                if (!double.IsNaN(leftAzimuthBoundary.Degree)) {
                    dataPoints.Add(new DomeShutterOpeningDataPoint() {
                        Azimuth = (leftAzimuthBoundary.Degree + 360.0) % 360.0,
                        MinAltitude = altitude,
                        MaxAltitude = 90.0
                    });
                    dataPoints.Add(new DomeShutterOpeningDataPoint() {
                        Azimuth = rightAzimuthBoundary.Degree % 360.0,
                        MinAltitude = altitude,
                        MaxAltitude = 90.0
                    });
                }

                if (double.IsNaN(leftAzimuthBoundary.Degree) && !fullCoverageReached) {
                    if (leftLimit == null) {
                        leftLimit = new DomeShutterOpeningDataPoint() {
                            Azimuth = 0,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        };
                    }
                    if (rightLimit == null) {
                        rightLimit = new DomeShutterOpeningDataPoint() {
                            Azimuth = 360.0,
                            MinAltitude = altitude,
                            MaxAltitude = 90.0
                        };
                    }
                    fullCoverageReached = true;
                }
                ct.ThrowIfCancellationRequested();
            }
            if (leftLimit != null) {
                dataPoints.Add(leftLimit);
            }
            if (rightLimit != null) {
                dataPoints.Add(rightLimit);
            }
            this.DomeShutterOpeningDataPoints = new AsyncObservableCollection<DomeShutterOpeningDataPoint>(dataPoints.OrderBy(dp => dp.Azimuth));
            this.DomeShutterOpeningDataPoints2.Clear();
        }
        */

        public ModelPointGenerationTypeEnum ModelPointGenerationType {
            get => this.modelBuilderOptions.ModelPointGenerationType;
            set {
                if (this.modelBuilderOptions.ModelPointGenerationType != value) {
                    this.modelBuilderOptions.ModelPointGenerationType = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int GoldenSpiralStarCount {
            get => this.modelBuilderOptions.GoldenSpiralStarCount;
            set {
                if (this.modelBuilderOptions.GoldenSpiralStarCount != value) {
                    this.modelBuilderOptions.GoldenSpiralStarCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int SiderealTrackStartOffsetSeconds {
            get => this.modelBuilderOptions.SiderealTrackStartOffsetSeconds;
            set {
                if (this.modelBuilderOptions.SiderealTrackStartOffsetSeconds != value) {
                    this.modelBuilderOptions.SiderealTrackStartOffsetSeconds = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int SiderealTrackEndOffsetSeconds {
            get => this.modelBuilderOptions.SiderealTrackEndOffsetSeconds;
            set {
                if (this.modelBuilderOptions.SiderealTrackEndOffsetSeconds != value) {
                    this.modelBuilderOptions.SiderealTrackEndOffsetSeconds = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double SiderealTrackRADeltaDegrees {
            get => this.modelBuilderOptions.SiderealTrackRADeltaDegrees;
            set {
                if (this.modelBuilderOptions.SiderealTrackRADeltaDegrees != value) {
                    this.modelBuilderOptions.SiderealTrackRADeltaDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool domeControlEnabled;

        public bool DomeControlEnabled {
            get => domeControlEnabled;
            private set {
                if (domeControlEnabled != value) {
                    domeControlEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool MinimizeDomeMovementEnabled {
            get => this.modelBuilderOptions.MinimizeDomeMovementEnabled;
            set {
                if (this.modelBuilderOptions.MinimizeDomeMovementEnabled != value) {
                    this.modelBuilderOptions.MinimizeDomeMovementEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int DomeShutterWidth_mm {
            get => this.modelBuilderOptions.DomeShutterWidth_mm;
            set {
                if (this.modelBuilderOptions.DomeShutterWidth_mm != value) {
                    this.modelBuilderOptions.DomeShutterWidth_mm = value;
                    RaisePropertyChanged();
                    _ = CalculateDomeShutterOpening(disconnectCts.Token);
                }
            }
        }

        private DataPoint scopePosition;

        public DataPoint ScopePosition {
            get => scopePosition;
            set {
                scopePosition = value;
                RaisePropertyChanged();
            }
        }

        private bool showNextUpDomeAzimuthPosition = false;

        public bool ShowNextUpDomeAzimuthPosition {
            get => showNextUpDomeAzimuthPosition;
            set {
                if (showNextUpDomeAzimuthPosition != value) {
                    showNextUpDomeAzimuthPosition = value;
                    RaisePropertyChanged();
                }
            }
        }

        private DataPoint nextUpDomeAzimuthPosition;

        public DataPoint NextUpDomeAzimuthPosition {
            get => nextUpDomeAzimuthPosition;
            set {
                nextUpDomeAzimuthPosition = value;
                RaisePropertyChanged();
            }
        }

        private CustomHorizon customHorizon = EMPTY_HORIZON;

        public CustomHorizon CustomHorizon {
            get => customHorizon;
            private set {
                if (value == null) {
                    customHorizon = EMPTY_HORIZON;
                } else {
                    customHorizon = value;
                }
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<DataPoint> horizonDataPoints = new AsyncObservableCollection<DataPoint>();

        public AsyncObservableCollection<DataPoint> HorizonDataPoints {
            get => horizonDataPoints;
            set {
                horizonDataPoints = value;
                RaisePropertyChanged();
            }
        }

        private Angle domeShutterAzimuthForOpening;

        private AsyncObservableCollection<DomeShutterOpeningDataPoint> domeShutterOpeningDataPoints = new AsyncObservableCollection<DomeShutterOpeningDataPoint>();

        public AsyncObservableCollection<DomeShutterOpeningDataPoint> DomeShutterOpeningDataPoints {
            get => domeShutterOpeningDataPoints;
            set {
                domeShutterOpeningDataPoints = value;
                RaisePropertyChanged();
            }
        }

        // If the dome shutter opening wraps around 360, we need a 2nd set of points to render the full dome slit exposure area
        private AsyncObservableCollection<DomeShutterOpeningDataPoint> domeShutterOpeningDataPoints2 = new AsyncObservableCollection<DomeShutterOpeningDataPoint>();

        public AsyncObservableCollection<DomeShutterOpeningDataPoint> DomeShutterOpeningDataPoints2 {
            get => domeShutterOpeningDataPoints2;
            set {
                domeShutterOpeningDataPoints2 = value;
                RaisePropertyChanged();
            }
        }

        private ImmutableList<ModelPoint> modelPoints = ImmutableList.Create<ModelPoint>();

        public ImmutableList<ModelPoint> ModelPoints {
            get => modelPoints;
            set {
                modelPoints = value;
                RaisePropertyChanged();
            }
        }

        private AsyncObservableCollection<ModelPoint> displayModelPoints = new AsyncObservableCollection<ModelPoint>();

        public AsyncObservableCollection<ModelPoint> DisplayModelPoints {
            get => displayModelPoints;
            set {
                displayModelPoints = value;
                RaisePropertyChanged();
            }
        }

        public bool WestToEastSorting {
            get => this.modelBuilderOptions.WestToEastSorting;
            set {
                if (this.modelBuilderOptions.WestToEastSorting != value) {
                    this.modelBuilderOptions.WestToEastSorting = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int BuilderNumRetries {
            get => this.modelBuilderOptions.BuilderNumRetries;
            set {
                if (this.modelBuilderOptions.BuilderNumRetries != value) {
                    this.modelBuilderOptions.BuilderNumRetries = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int MaxFailedPoints {
            get => this.modelBuilderOptions.MaxFailedPoints;
            set {
                if (this.modelBuilderOptions.MaxFailedPoints != value) {
                    this.modelBuilderOptions.MaxFailedPoints = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double MaxPointRMS {
            get => this.modelBuilderOptions.MaxPointRMS;
            set {
                if (this.modelBuilderOptions.MaxPointRMS != value) {
                    this.modelBuilderOptions.MaxPointRMS = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool buildInProgress;

        public bool BuildInProgress {
            get => buildInProgress;
            private set {
                if (buildInProgress != value) {
                    buildInProgress = value;
                    RaisePropertyChanged();
                }
            }
        }

        public ICommand ClearPointsCommand { get; private set; }
        public ICommand GeneratePointsCommand { get; private set; }
        public ICommand BuildCommand { get; private set; }
        public ICommand CancelBuildCommand { get; private set; }
        public ICommand StopBuildCommand { get; private set; }
    }
}