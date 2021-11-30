#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron.Converters;
using NINA.Profile.Interfaces;
using System.ComponentModel;

namespace NINA.Joko.Plugin.TenMicron.ModelBuilder {

    [TypeConverter(typeof(EnumStaticDescriptionTypeConverter))]
    public enum ModelPointStateEnum {

        [Description("Generated")]
        Generated = 0,

        [Description("Next")]
        UpNext = 1,

        [Description("Exposing Image")]
        Exposing = 2,

        [Description("Processing")]
        Processing = 3,

        [Description("Failed")]
        Failed = 4,

        [Description("Below Horizon")]
        BelowHorizon = 5,
    }

    public class ModelPoint : BaseINPC {
        private readonly ICustomDateTime dateTime;
        private readonly ITelescopeMediator telescopeMediator;

        public ModelPoint(ICustomDateTime dateTime, ITelescopeMediator telescopeMediator) {
            this.dateTime = dateTime;
            this.telescopeMediator = telescopeMediator;
        }

        private int modelIndex;

        public int ModelIndex {
            get => modelIndex;
            set {
                if (modelIndex != value) {
                    modelIndex = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double altitude;

        public double Altitude {
            get => altitude;
            set {
                if (altitude != value) {
                    altitude = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double azimuth;

        public double Azimuth {
            get => azimuth;
            set {
                if (azimuth != value) {
                    azimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        private ModelPointStateEnum modelPointState;

        public ModelPointStateEnum ModelPointState {
            get => modelPointState;
            set {
                if (modelPointState != value) {
                    modelPointState = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(ModelPointStateString));
                }
            }
        }

        public string ModelPointStateString {
            get {
                var fi = typeof(ModelPointStateEnum).GetField(ModelPointState.ToString());
                var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);
                return attributes[0].Description;
            }
        }

        private AstrometricTime mountReportedLocalSiderealTime = AstrometricTime.ZERO;

        public AstrometricTime MountReportedLocalSiderealTime {
            get => mountReportedLocalSiderealTime;
            set {
                mountReportedLocalSiderealTime = value;
                RaisePropertyChanged();
            }
        }

        private AstrometricTime mountReportedRightAscension = AstrometricTime.ZERO;

        public AstrometricTime MountReportedRightAscension {
            get => mountReportedRightAscension;
            set {
                mountReportedRightAscension = value;
                RaisePropertyChanged();
            }
        }

        private CoordinateAngle mountReportedDeclination = CoordinateAngle.ZERO;

        public CoordinateAngle MountReportedDeclination {
            get => mountReportedDeclination;
            set {
                mountReportedDeclination = value;
                RaisePropertyChanged();
            }
        }

        private Coordinates plateSolvedCoordinates;

        public Coordinates PlateSolvedCoordinates {
            get => plateSolvedCoordinates;
            set {
                plateSolvedCoordinates = value.Transform(Epoch.JNOW);
                RaisePropertyChanged();
            }
        }

        public TopocentricCoordinates ToTopocentric() {
            var telescopeInfo = telescopeMediator.GetInfo();
            return new TopocentricCoordinates(
                azimuth: Angle.ByDegree(azimuth),
                altitude: Angle.ByDegree(altitude),
                latitude: Angle.ByDegree(telescopeInfo.SiteLatitude),
                longitude: Angle.ByDegree(telescopeInfo.SiteLongitude),
                elevation: telescopeInfo.SiteElevation,
                dateTime: dateTime);
        }

        public Coordinates ToCelestial(double pressurehPa, double tempCelcius, double relativeHumidity) {
            return ToTopocentric().Transform(Epoch.JNOW, pressurehPa: pressurehPa, tempCelcius: tempCelcius, relativeHumidity: relativeHumidity, wavelength: 0.54);
        }

        public override string ToString() {
            return JsonConvert.SerializeObject(this);
        }
    }
}