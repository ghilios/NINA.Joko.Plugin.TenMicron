#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Utility;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Joko.Plugin.TenMicron.Model;
using System.Windows;
using NINA.Equipment.Interfaces;
using System.Threading;
using System.Net;
using NINA.Core.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugin.TenMicron.ViewModels {

    [Export(typeof(IDockableVM))]
    public class MountVM : DockableVM, IMountVM, ITelescopeConsumer {

        private readonly SynchronizationContext synchronizationContext =
            Application.Current?.Dispatcher != null
            ? new DispatcherSynchronizationContext(Application.Current.Dispatcher)
            : null;

        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IMount mount;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IMountMediator mountMediator;
        private readonly ITenMicronOptions options;
        private IProgress<ApplicationStatus> progress;
        private DeviceUpdateTimer updateTimer;
        private volatile MountValuePoller poller;
        private bool disposed = false;
        private bool supportedMountConnected = false;
        private bool previousTelescopeConnected = false;
        private bool previousSlewing = false;
        private bool previousAtPark = false;
        private bool previousAtHome = false;
        private bool previousTrackingEnabled = false;
        private TrackingMode? previousTrackingMode = null;

        // Settings that only change when someone changes them. Writes through IMount invalidate them right away, so
        // this only bounds how long a change made from the handpad or other software takes to show up
        private static readonly TimeSpan SettingsPollInterval = TimeSpan.FromSeconds(60);

        [ImportingConstructor]
        public MountVM(IProfileService profileService, ITelescopeMediator telescopeMediator, IApplicationStatusMediator applicationStatusMediator) :
            this(profileService, telescopeMediator, applicationStatusMediator, TenMicronPlugin.Mount, TenMicronPlugin.MountMediator, TenMicronPlugin.TenMicronOptions) {
        }

        public MountVM(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IMount mount,
            IMountMediator mountMediator,
            ITenMicronOptions options) : base(profileService) {
            this.Title = "10u Mount Info";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugin.TenMicron;component/Resources/SVGDataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["TenMicronSVG"];
            ImageGeometry.Freeze();

            this.mount = mount;
            this.telescopeMediator = telescopeMediator;
            this.mountMediator = mountMediator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.options = options;

            MountInfo.Status = MountStatusEnum.NotConnected;

            if (SynchronizationContext.Current == synchronizationContext) {
                this.progress = new Progress<ApplicationStatus>(p => {
                    p.Source = this.Title;
                    this.applicationStatusMediator.StatusUpdate(p);
                });
            } else {
                synchronizationContext.Send(_ => {
                    this.progress = new Progress<ApplicationStatus>(p => {
                        p.Source = this.Title;
                        this.applicationStatusMediator.StatusUpdate(p);
                    });
                }, null);
            }

            this.telescopeMediator.RegisterConsumer(this);
            this.mountMediator.RegisterHandler(this);

            ResetMeridianSlewLimitCommand = new RelayCommand(ResetMeridianSlewLimit);
            ResetSlewSettleLimitCommand = new RelayCommand(ResetSlewSettleTime);
            TogglePowerCommand = new AsyncRelayCommand(() => Task.Run(TogglePower));
        }

        private void BroadcastMountInfo() {
            this.mountMediator.Broadcast(MountInfo);
        }

        private void ResetMeridianSlewLimit() {
            try {
                if (this.mount.SetMeridianSlewLimit(0)) {
                    MountInfo.MeridianLimitDegrees = 0;
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to reset meridian limit: {e.Message}");
                Logger.Error(e);
            }
        }

        private void ResetSlewSettleTime() {
            try {
                if (this.mount.SetSlewSettleTime(decimal.Zero)) {
                    MountInfo.SlewSettleTimeSeconds = decimal.Zero;
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to reset slew settle time: {e.Message}");
                Logger.Error(e);
            }
        }

        private Task<bool> TogglePower() {
            if (MountInfo.Connected) {
                return Task.FromResult(Shutdown());
            } else {
                return PowerOn(CancellationToken.None);
            }
        }

        public async Task<bool> PowerOn(CancellationToken ct) {
            if (MountInfo.Connected) {
                return true;
            }

            Task progressDelay = null;
            CancellationTokenSource timeoutCts = null;
            try {
                if (string.IsNullOrEmpty(this.Options.IPAddress) || string.IsNullOrEmpty(this.Options.MACAddress)) {
                    Notification.ShowError("Cannot power on mount. No IP address is set. If you connect via IP, connect at least once to initialize settings");
                    Logger.Error("Cannot power on mount. No IP address is set. If you connect via IP, connect at least once to initialize settings");
                    return false;
                }

                if (string.IsNullOrEmpty(this.Options.DriverID)) {
                    Notification.ShowError("Cannot power on mount. Connect at least once to initialize settings");
                    Logger.Error("Cannot power on mount. Connect at least once to initialize settings");
                    return false;
                }

                this.progress.Report(new ApplicationStatus() {
                    Status = "Sending WOL packet"
                });

                progressDelay = Task.Delay(1000, ct);
                var ipAddress = IPAddress.Parse(this.Options.IPAddress);
                var mac = this.Options.MACAddress;
                await MountUtility.WakeOnLan(mac, this.Options.WolBroadcastIP, ct);

                await progressDelay;
                this.progress.Report(new ApplicationStatus() {
                    Status = "Waiting for mount to power on"
                });

                progressDelay = Task.Delay(1000, ct);
                timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
                await MountUtility.WaitUntilResponding(ipAddress, this.Options.Port, linkedCt.Token);

                profileService.ActiveProfile.TelescopeSettings.Id = this.Options.DriverID;
                await telescopeMediator.Rescan();
                return await telescopeMediator.Connect();
            } catch (OperationCanceledException) {
                if (timeoutCts?.IsCancellationRequested == true) {
                    Notification.ShowError("Timed out waiting for mount to power on");
                    Logger.Error("Timed out waiting for mount to power on");
                } else {
                    Logger.Info("10u power on cancelled");
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to power on 10u mount: {e.Message}");
                Logger.Error("Failed to power on 10u mount", e);
            } finally {
                if (progressDelay != null) {
                    await progressDelay;
                }
                this.progress.Report(new ApplicationStatus() { });
            }
            return false;
        }

        public void Dispose() {
            if (!this.disposed) {
                updateTimer?.Stop();
                StopPolling();
                this.telescopeMediator.RemoveConsumer(this);
                this.disposed = true;
            }
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            InvalidateOnTelescopeStateChange(deviceInfo);
            if (previousTelescopeConnected == deviceInfo.Connected) {
                return;
            }

            try {
                if (deviceInfo.Connected) {
                    try {
                        ProductFirmware productFirmware = null;
                        try {
                            productFirmware = mount.GetProductFirmware();
                        } catch (Exception e) {
                            Logger.Error("Failed to query product firmware after telescope connected", e);
                            Notification.ShowWarning($"Not a 10u mount. 10u utilities disabled.");
                            return;
                        }

                        if (!MountUtility.IsSupportedProduct(productFirmware)) {
                            Logger.Error($"{productFirmware.ProductName} is not a supported 10u mount. 10u utilities disabled");
                            Notification.ShowInformation($"{productFirmware.ProductName} is not a supported 10u mount. 10u utilities disabled");
                            return;
                        }

                        var ascomConfig = MountUtility.GetMountAscomConfig(deviceInfo.DeviceId);
                        if (ascomConfig != null) {
                            if (!MountUtility.ValidateMountAscomConfig(ascomConfig)) {
                                Logger.Error($"ASCOM configuration validation failed. Leaving 10u mount utilities disconnected");
                                return;
                            }

                            RefractionOverrideFilePath = ascomConfig.RefractionUpdateFile;
                        }

                        mount.SetMaximumPrecision(productFirmware);
                        MountInfo.ProductFirmware = productFirmware;
                        MountInfo.MountId = mount.GetId();
                        MountInfo.Status = MountStatusEnum.Unknown;
                        Options.DriverID = profileService.ActiveProfile.TelescopeSettings.Id;

                        UpdateAddressConfig();
                        supportedMountConnected = true;
                        MountInfo.Connected = true;

                        // This cannot be initialized in the constructor because it is running in an Async context at that time, which causes UpdateMountValues to never fire
                        _ = updateTimer?.Stop();
                        StartPolling();
                        updateTimer = new DeviceUpdateTimer(
                            GetMountValues,
                            UpdateMountValues,
                            profileService.ActiveProfile.ApplicationSettings.DevicePollingInterval
                        );
                        updateTimer.Run();
                    } catch (Exception e) {
                        Notification.ShowError($"Failed to connect 10u utilities. {e.Message}");
                    }
                } else {
                    _ = updateTimer?.Stop();
                    StopPolling();

                    MountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();
                    supportedMountConnected = false;
                }
            } finally {
                previousTelescopeConnected = deviceInfo.Connected;
            }
        }

        private void StartPolling() {
            StopPolling();

            var newPoller = new MountValuePoller();
            // Status changes constantly, and reading it every tick doubles as the connection heartbeat
            newPoller.Register(nameof(MountInfo.Status), () => this.mount.GetStatus().Value, TimeSpan.Zero);
            newPoller.Register(nameof(MountInfo.UnattendedFlipEnabled), () => this.mount.GetUnattendedFlipEnabled().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.TrackingRateArcsecPerSec), () => this.mount.GetTrackingRateArcsecsPerSec().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.SlewSettleTimeSeconds), () => this.mount.GetSlewSettleTimeSeconds().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.MeridianLimitDegrees), () => this.mount.GetMeridianSlewLimitDegrees().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.DualAxisTrackingEnabled), () => this.mount.GetDualAxisTrackingEnabled().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.RefractionCorrectionEnabled), () => this.mount.GetRefractionCorrectionEnabled().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.RefractionTemperature), () => this.mount.GetTemperature().Value, SettingsPollInterval);
            newPoller.Register(nameof(MountInfo.RefractionPressure), () => this.mount.GetPressure().Value, SettingsPollInterval);
            poller = newPoller;
            this.mount.ValueWritten += Mount_ValueWritten;
        }

        private void StopPolling() {
            this.mount.ValueWritten -= Mount_ValueWritten;
            poller = null;
        }

        private void Mount_ValueWritten(object sender, string mountInfoProperty) {
            poller?.Invalidate(mountInfoProperty);
        }

        /// <summary>
        /// NINA polls the telescope through ASCOM anyway, so use its state transitions to refresh the 10u values they affect
        /// without waiting for the next scheduled read
        /// </summary>
        private void InvalidateOnTelescopeStateChange(TelescopeInfo deviceInfo) {
            TrackingMode? trackingMode = deviceInfo.TrackingRate.TrackingMode;
            var currentPoller = poller;
            if (currentPoller != null && deviceInfo.Connected) {
                if (deviceInfo.Slewing != previousSlewing
                    || deviceInfo.AtPark != previousAtPark
                    || deviceInfo.AtHome != previousAtHome
                    || deviceInfo.TrackingEnabled != previousTrackingEnabled) {
                    currentPoller.Invalidate(nameof(MountInfo.Status));
                }
                if (deviceInfo.TrackingEnabled != previousTrackingEnabled || trackingMode != previousTrackingMode) {
                    currentPoller.Invalidate(nameof(MountInfo.TrackingRateArcsecPerSec));
                }
            }

            previousSlewing = deviceInfo.Slewing;
            previousAtPark = deviceInfo.AtPark;
            previousAtHome = deviceInfo.AtHome;
            previousTrackingEnabled = deviceInfo.TrackingEnabled;
            previousTrackingMode = trackingMode;
        }

        private void UpdateAddressConfig() {
            try {
                var ipAddress = mount.GetIPAddress();
                var macAddress = mount.GetMACAddress();
                options.IPAddress = ipAddress.Value.IP;
                options.MACAddress = macAddress;
            } catch (Exception e) {
                Logger.Warning($"Failed to get IP and MAC address from 10u mount. {e.Message}");
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

        public ITenMicronOptions Options => options;

        private string refractionOverrideFilePath;

        public string RefractionOverrideFilePath {
            get => refractionOverrideFilePath;
            set {
                if (refractionOverrideFilePath != value) {
                    this.refractionOverrideFilePath = value;
                    RaisePropertyChanged();
                }
            }
        }

        public async Task Disconnect() {
            if (updateTimer != null) {
                await updateTimer.Stop();
            }
            StopPolling();

            MountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();
            MountInfo.Status = MountStatusEnum.NotConnected;
            supportedMountConnected = false;

            BroadcastMountInfo();
        }

        private static bool TryGetMountValue<T>(Dictionary<string, object> mountValues, string key, out T value) {
            if (mountValues.TryGetValue(key, out var o) && o is T typed) {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        private void UpdateMountValues(Dictionary<string, object> mountValues) {
            TryGetMountValue(mountValues, nameof(MountInfo.Connected), out bool connected);
            MountInfo.Connected = connected;

            if (!MountInfo.Connected) {
                _ = Disconnect();
                return;
            }

            // Each tick only carries the values the poller read, so keep the cached value for anything missing
            if (TryGetMountValue(mountValues, nameof(MountInfo.UnattendedFlipEnabled), out bool unattendedFlipEnabled)) {
                MountInfo.UnattendedFlipEnabled = unattendedFlipEnabled;
                UnattendedFlipEnabled = unattendedFlipEnabled;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.TrackingRateArcsecPerSec), out decimal trackingRate)) {
                MountInfo.TrackingRateArcsecPerSec = trackingRate;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.Status), out MountStatusEnum status)) {
                MountInfo.Status = status;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.SlewSettleTimeSeconds), out decimal slewSettleTime)) {
                MountInfo.SlewSettleTimeSeconds = slewSettleTime;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.MeridianLimitDegrees), out int meridianLimit)) {
                MountInfo.MeridianLimitDegrees = meridianLimit;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.DualAxisTrackingEnabled), out bool dualAxisTracking)) {
                MountInfo.DualAxisTrackingEnabled = dualAxisTracking;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.RefractionCorrectionEnabled), out bool refractionCorrection)) {
                MountInfo.RefractionCorrectionEnabled = refractionCorrection;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.RefractionTemperature), out decimal refractionTemperature)) {
                MountInfo.RefractionTemperature = refractionTemperature;
            }
            if (TryGetMountValue(mountValues, nameof(MountInfo.RefractionPressure), out decimal refractionPressure)) {
                MountInfo.RefractionPressure = refractionPressure;
            }

            BroadcastMountInfo();
        }

        private Dictionary<string, object> GetMountValues() {
            var mountValues = new Dictionary<string, object>();
            try {
                var currentPoller = poller;
                if (!supportedMountConnected || currentPoller == null) {
                    return mountValues;
                }

                foreach (var polledValue in currentPoller.Poll()) {
                    mountValues.Add(polledValue.Key, polledValue.Value);
                }
                mountValues.Add(nameof(MountInfo.Connected), true);
                return mountValues;
            } catch (Exception e) {
                if (telescopeMediator.GetInfo().Connected) {
                    Notification.ShowError($"Failed to retrieve mount properties. 10u mount utilities disconnected");
                    Logger.Error("Failed while retrieving 10u mount properties", e);
                    MountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();
                }

                mountValues.Clear();
                mountValues.Add(nameof(MountInfo.Connected), false);
                return mountValues;
            }
        }

        public ICommand ResetMeridianSlewLimitCommand { get; private set; }
        public ICommand ResetSlewSettleLimitCommand { get; private set; }
        public ICommand TogglePowerCommand { get; private set; }

        private bool unattendedFlipEnabled;

        public bool UnattendedFlipEnabled {
            get => unattendedFlipEnabled;
            set {
                if (unattendedFlipEnabled != value) {
                    unattendedFlipEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        public void DisableUnattendedFlip() {
            try {
                mount.SetUnattendedFlip(false);
                MountInfo.UnattendedFlipEnabled = false;
                UnattendedFlipEnabled = false;
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError($"Failed to disable unattended flip: {ex.Message}");
            }
        }

        public void SetDualAxisTracking(bool enabled) {
            if (!MountInfo.Connected) {
                return;
            }

            try {
                if (mount.SetDualAxisTracking(enabled)) {
                    MountInfo.DualAxisTrackingEnabled = enabled;
                }
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError($"Failed to set dual axis tracking to {enabled}: {ex.Message}");
            }
        }

        public void SetRefactionCorrectionEnabled(bool enabled) {
            if (!MountInfo.Connected) {
                return;
            }

            try {
                if (mount.SetRefractionCorrection(enabled)) {
                    MountInfo.RefractionCorrectionEnabled = enabled;
                }
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError($"Failed to set refraction correction to {enabled}: {ex.Message}");
            }
        }

        public Task<IList<string>> Rescan() {
            throw new NotImplementedException();
        }

        public Task<bool> Connect() {
            throw new NotImplementedException();
        }

        public MountInfo GetDeviceInfo() {
            return MountInfo;
        }

        public CoordinateAngle GetMountReportedDeclination() {
            if (supportedMountConnected) {
                return mount.GetDeclination();
            }
            return null;
        }

        public AstrometricTime GetMountReportedRightAscension() {
            if (supportedMountConnected) {
                return mount.GetRightAscension();
            }
            return null;
        }

        public AstrometricTime GetMountReportedLocalSiderealTime() {
            if (supportedMountConnected) {
                return mount.GetLocalSiderealTime();
            }
            return null;
        }

        public bool SetTrackingRate(TrackingMode trackingMode) {
            if (supportedMountConnected) {
                switch (trackingMode) {
                    case TrackingMode.Sidereal:
                        mount.SetSiderealTrackingRate();
                        mount.StartTracking();
                        mount.SetDualAxisTracking(true);
                        return true;

                    case TrackingMode.Solar:
                        mount.SetSolarTrackingRate();
                        mount.StartTracking();
                        mount.SetDualAxisTracking(true);
                        return true;

                    case TrackingMode.Lunar:
                        mount.SetLunarTrackingRate();
                        mount.StartTracking();
                        mount.SetDualAxisTracking(true);
                        return true;

                    case TrackingMode.Stopped:
                        mount.StopTracking();
                        return true;

                    case TrackingMode.King:
                        Logger.Error("King rate is not supported");
                        return false;

                    case TrackingMode.Custom:
                        Logger.Error("Custom rate is not supported");
                        return false;
                }

                throw new ArgumentException($"Unknown tracking mode received: {trackingMode}");
            }
            return false;
        }

        public bool Shutdown() {
            if (MountInfo.Connected) {
                if (mount.Shutdown()) {
                    _ = Disconnect();
                    return true;
                } else {
                    Notification.ShowError("Failed to send a shutdown command to the 10u mount");
                    Logger.Error("Failed to send a shutdown command to the 10u mount");
                }
            }
            return false;
        }

        public string Action(string actionName, string actionParameters) {
            throw new NotImplementedException();
        }

        public string SendCommandString(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public bool SendCommandBool(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public void SendCommandBlind(string command, bool raw = true) {
            throw new NotImplementedException();
        }

        public IDevice GetDevice() {
            throw new NotImplementedException();
        }

        public bool SetTemperature(double tempCelcius) {
            return mount.SetTemperature(tempCelcius);
        }

        public bool SetPressure(double pressureHpa) {
            return mount.SetPressure(pressureHpa);
        }
    }
}