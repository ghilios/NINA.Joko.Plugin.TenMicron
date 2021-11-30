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
using NINA.Joko.Plugin.TenMicron.ModelBuilder;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using System;
using System.Collections.Generic;
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
        private readonly IDomeSynchronization domeSynchronization;
        private readonly IModelAccessor modelAccessor;
        private readonly IModelPointGenerator modelPointGenerator;
        private readonly ICustomDateTime dateTime;

        private readonly ITenMicronOptions modelBuilderOptions;
        private IProgress<ApplicationStatus> progress;
        private bool disposed = false;
        private CancellationTokenSource disconnectCts;

        [ImportingConstructor]
        public MountModelBuilderVM(IProfileService profileService, IApplicationStatusMediator applicationStatusMediator, ITelescopeMediator telescopeMediator, IDomeMediator domeMediator, IDomeSynchronization domeSynchronization) :
            this(profileService,
                TenMicronPlugin.TenMicronOptions,
                telescopeMediator,
                domeMediator,
                applicationStatusMediator,
                domeSynchronization,
                TenMicronPlugin.MountMediator,
                new ModelAccessor(telescopeMediator, TenMicronPlugin.MountMediator, new SystemDateTime()),
                new ModelPointGenerator(new SystemDateTime(), telescopeMediator),
                new SystemDateTime()) {
        }

        public MountModelBuilderVM(
            IProfileService profileService,
            ITenMicronOptions modelBuilderOptions,
            ITelescopeMediator telescopeMediator,
            IDomeMediator domeMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IDomeSynchronization domeSynchronization,
            IMountMediator mountMediator,
            IModelAccessor modelAccessor,
            IModelPointGenerator modelPointGenerator,
            ICustomDateTime dateTime) : base(profileService) {
            this.Title = "10u Model Builder";
            this.modelBuilderOptions = modelBuilderOptions;
            this.applicationStatusMediator = applicationStatusMediator;
            this.mountMediator = mountMediator;
            this.telescopeMediator = telescopeMediator;
            this.domeMediator = domeMediator;
            this.domeSynchronization = domeSynchronization;
            this.modelAccessor = modelAccessor;
            this.modelPointGenerator = modelPointGenerator;
            this.dateTime = dateTime;
            this.disconnectCts = new CancellationTokenSource();

            this.telescopeMediator.RegisterConsumer(this);
            this.domeMediator.RegisterConsumer(this);
            this.mountMediator.RegisterConsumer(this);

            this.profileService.ProfileChanged += ProfileService_ProfileChanged;
            this.profileService.ActiveProfile.AstrometrySettings.PropertyChanged += AstrometrySettings_PropertyChanged;
            this.LoadHorizon();

            this.GeneratePointsCommand = new AsyncCommand<bool>(GeneratePoints);
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            this.LoadHorizon();
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

            this.disconnectCts?.Cancel();
            this.disconnectCts = new CancellationTokenSource();
            this.domeShutterAzimuthForOpening = null;
            this.DomeShutterOpeningDataPoints.Clear();
            this.DomeShutterOpeningDataPoints2.Clear();
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

        private bool GenerateGoldenSpiral(int goldenSpiralStarCount) {
            var modelPoints = this.modelPointGenerator.GenerateGoldenSpiral(goldenSpiralStarCount, this.CustomHorizon);
            this.ModelPoints = new AsyncObservableCollection<ModelPoint>(modelPoints);
            return true;
        }

        private bool GenerateSiderealPath() {
            Notification.ShowError("Sidereal Path not yet implemented");
            return false;
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
                var radiusAtAltitude = Math.Cos(altitudeAngle.Radians) * domeRadius;
                var oppositeOverHypotenuse = 0.5 * DomeShutterWidth_mm / radiusAtAltitude;

                /*
                 * TODO: This should be used during point building
                var latitude = Angle.ByDegree(TelescopeInfo.SiteLatitude);
                var longitude = Angle.ByDegree(TelescopeInfo.SiteLongitude);
                var elevation = TelescopeInfo.SiteElevation;
                var lst = TelescopeInfo.SiderealTime;
                var sideOfPier = azimuth <= 180.0 ? PierSide.pierEast : PierSide.pierWest;
                var topocentricCoordinates = new TopocentricCoordinates(azimuth: azimuthAngle, altitude: altitudeAngle, latitude: latitude, longitude: longitude, dateTime: this.dateTime);
                var equatorialCoordinates = topocentricCoordinates.Transform(Epoch.JNOW);
                var domeCoordinates = this.domeSynchronization.TargetDomeCoordinates(equatorialCoordinates, lst, siteLatitude: latitude, siteLongitude: longitude, sideOfPier: sideOfPier);
                */

                var apertureAngleThreshold = Angle.ByRadians(Math.Asin(oppositeOverHypotenuse));
                var leftAzimuthBoundary = azimuthAngle - apertureAngleThreshold;
                var rightAzimuthBoundary = azimuthAngle + apertureAngleThreshold;

                if (leftAzimuthBoundary.Degree < 0.0 || rightAzimuthBoundary.Degree > 360.0) {
                    double boundaryAltitude;
                    if (leftAzimuthBoundary.Degree < 0.0) {
                        boundaryAltitude = altitude - altitudeDelta * (1.0d - azimuthAngle.Degree / apertureAngleThreshold.Degree);
                    } else {
                        boundaryAltitude = altitude - altitudeDelta * (1.0d - (360.0d - azimuthAngle.Degree) / apertureAngleThreshold.Degree);
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
                if (oppositeOverHypotenuse < 1.0) {
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

                if (oppositeOverHypotenuse >= 1.0 && !fullCoverageReached) {
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

        private CustomHorizon customHorizon = EMPTY_HORIZON;

        public CustomHorizon CustomHorizon {
            get => customHorizon;
            private set {
                if (customHorizon == null) {
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

        private AsyncObservableCollection<ModelPoint> modelPoints = new AsyncObservableCollection<ModelPoint>();

        public AsyncObservableCollection<ModelPoint> ModelPoints {
            get => modelPoints;
            set {
                modelPoints = value;
                RaisePropertyChanged();
            }
        }

        public ICommand GeneratePointsCommand { get; private set; }
    }
}