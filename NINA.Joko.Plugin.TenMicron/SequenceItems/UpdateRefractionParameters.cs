#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ASCOM.Common.DeviceInterfaces;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Update Refraction Parameters")]
    [ExportMetadata("Description", "Updates refraction parameters on the 10 Micron mount using a connected weather device")]
    [ExportMetadata("Icon", "MoveFocuserByTemperatureSVG")]
    [ExportMetadata("Category", "10 Micron")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class UpdateRefractionParameters : SequenceItem, IValidatable {
        private readonly IWeatherDataMediator weatherDataMediator;
        private readonly IMountMediator mountMediator;

        [ImportingConstructor]
        public UpdateRefractionParameters(IWeatherDataMediator weatherDataMediator) : this(weatherDataMediator, TenMicronPlugin.MountMediator) {
        }

        public UpdateRefractionParameters(IWeatherDataMediator weatherDataMediator, IMountMediator mountMediator) {
            this.weatherDataMediator = weatherDataMediator;
            this.mountMediator = mountMediator;
        }

        private UpdateRefractionParameters(UpdateRefractionParameters cloneMe) : this(cloneMe.weatherDataMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new UpdateRefractionParameters(this);
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var weatherInfo = weatherDataMediator.GetInfo();
            if (!weatherInfo.Connected) {
                throw new SequenceEntityFailedException(Loc.Instance["LblWeatherNoSource"]);
            }
            if (!mountMediator.GetInfo().Connected) {
                throw new SequenceEntityFailedException(Loc.Instance["10u mount not connected"]);
            }

            mountMediator.SetTemperature(weatherInfo.Temperature);
            mountMediator.SetPressure(weatherInfo.Pressure);
        }

        public bool Validate() {
            var i = new List<string>();
            if (!mountMediator.GetInfo().Connected) {
                i.Add("10u mount not connected");
            }
            if (!weatherDataMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblWeatherNoSource"]);
            }

            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(UpdateRefractionParameters)}";
        }
    }
}