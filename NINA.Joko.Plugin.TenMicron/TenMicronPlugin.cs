﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Properties;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows.Input;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces;
using NINA.Joko.Plugin.TenMicron.ModelManagement;
using NINA.PlateSolving.Interfaces;

namespace NINA.Joko.Plugin.TenMicron {

    /// <summary>
    /// Longer term consideration TODO list:
    ///  1. Split download time from exposure to avoid waiting for download before slewing to the next point
    ///  2. Minimize distance between points instead of going purely based on azimuth
    ///  3. Use AltAz slew on the mount instead of calculating our own refaction-adjusted RA/DEC
    ///  4. Option to save failed points and images used to plate solve
    ///
    /// Short term TODO list:
    ///  1. Add Min and Max Altitude for point generation. This will supplement the horizon
    ///  2. Retries, including max RMS
    ///  3. Sidereal track
    ///  4. Plugins to trigger model build
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class TenMicronPlugin : PluginBase {

        [ImportingConstructor]
        public TenMicronPlugin(
            IProfileService profileService, ITelescopeMediator telescopeMediator, IApplicationStatusMediator applicationStatusMediator, IDomeMediator domeMediator, IDomeSynchronization domeSynchronization,
            IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator, IWeatherDataMediator weatherDataMediator, ICameraMediator cameraMediator) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            if (TenMicronOptions == null) {
                TenMicronOptions = new TenMicronOptions(profileService);
            }

            ResetModelBuilderDefaultsCommand = new RelayCommand((object o) => TenMicronOptions.ResetDefaults());

            MountCommander = new TelescopeMediatorMountCommander(telescopeMediator, TenMicronOptions);
            Mount = new Mount(MountCommander);
            MountMediator = new MountMediator();
            MountModelMediator = new MountModelMediator();
            DateTime = new SystemDateTime();
            ModelAccessor = new ModelAccessor(telescopeMediator, MountModelMediator, DateTime);
            ModelPointGenerator = new ModelPointGenerator(DateTime, telescopeMediator);
            ModelBuilder = new ModelBuilder(profileService, MountModelMediator, Mount, telescopeMediator, domeMediator, cameraMediator, domeSynchronization, plateSolverFactory, imagingMediator, filterWheelMediator, weatherDataMediator);
        }

        public static TenMicronOptions TenMicronOptions { get; private set; }

        public ICommand ResetModelBuilderDefaultsCommand { get; private set; }

        public static IMountCommander MountCommander { get; private set; }

        public static IMount Mount { get; private set; }

        public static IModelAccessor ModelAccessor { get; private set; }

        public static IModelBuilder ModelBuilder { get; private set; }

        public static ICustomDateTime DateTime { get; private set; }

        public static IMountMediator MountMediator { get; private set; }

        public static IMountModelMediator MountModelMediator { get; private set; }

        public static IModelPointGenerator ModelPointGenerator { get; private set; }
    }
}