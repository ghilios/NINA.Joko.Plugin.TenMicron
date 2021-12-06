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
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron.Converters;
using NINA.Joko.Plugin.TenMicron.Model;
using System;
using System.ComponentModel;
using System.Globalization;

namespace NINA.Joko.Plugin.TenMicron.Model {

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

        [Description("Added to Model")]
        AddedToModel = 4,

        [Description("Failed")]
        Failed = 97,

        [Description("High RMS")]
        FailedRMS = 98,

        [Description("Outside Altitude Bounds")]
        OutsideAltitudeBounds = 99,

        [Description("Below Horizon")]
        BelowHorizon = 100,
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

        private double minDomeAzimuth = double.NaN;

        public double MinDomeAzimuth {
            get => minDomeAzimuth;
            set {
                if (minDomeAzimuth != value) {
                    minDomeAzimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double domeAzimuth = double.NaN;

        public double DomeAzimuth {
            get => domeAzimuth;
            set {
                if (domeAzimuth != value) {
                    domeAzimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double maxDomeAzimuth = double.NaN;

        public double MaxDomeAzimuth {
            get => maxDomeAzimuth;
            set {
                if (maxDomeAzimuth != value) {
                    maxDomeAzimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        private Coordinates coordinates;

        public Coordinates Coordinates {
            get => coordinates;
            set {
                coordinates = value;
                RaisePropertyChanged();
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

        private PierSide mountReportedSideOfPier = PierSide.pierUnknown;

        public PierSide MountReportedSideOfPier {
            get => mountReportedSideOfPier;
            set {
                if (mountReportedSideOfPier != value) {
                    mountReportedSideOfPier = value;
                    RaisePropertyChanged();
                }
            }
        }

        private Coordinates plateSolvedCoordinates;

        public Coordinates PlateSolvedCoordinates {
            get => plateSolvedCoordinates;
            set {
                plateSolvedCoordinates = value?.Transform(Epoch.JNOW);
                RaisePropertyChanged();
            }
        }

        private AstrometricTime plateSolvedRightAscension = AstrometricTime.ZERO;

        public AstrometricTime PlateSolvedRightAscension {
            get => plateSolvedRightAscension;
            set {
                plateSolvedRightAscension = value;
                RaisePropertyChanged();
            }
        }

        private CoordinateAngle plateSolvedDeclination = CoordinateAngle.ZERO;

        public CoordinateAngle PlateSolvedDeclination {
            get => plateSolvedDeclination;
            set {
                plateSolvedDeclination = value;
                RaisePropertyChanged();
            }
        }

        private double rmsError = double.NaN;

        public double RMSError {
            get => rmsError;
            set {
                if (rmsError != value) {
                    rmsError = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(RMSErrorString));
                }
            }
        }

        public string RMSErrorString {
            get {
                if (double.IsNaN(rmsError)) {
                    return "-";
                }
                return rmsError.ToString("0.0##", CultureInfo.CurrentUICulture);
            }
        }

        private DateTime captureTime = DateTime.MinValue;

        public DateTime CaptureTime {
            get => captureTime;
            set {
                if (captureTime != value) {
                    captureTime = value;
                    RaisePropertyChanged();
                }
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

        public Coordinates ToCelestial(double pressurehPa, double tempCelcius, double relativeHumidity, double wavelength) {
            return ToTopocentric().Transform(Epoch.JNOW, pressurehPa: pressurehPa, tempCelcius: tempCelcius, relativeHumidity: relativeHumidity, wavelength: wavelength);
        }

        public override string ToString() {
            return $"Alt={Altitude}, Az={Azimuth}, State={ModelPointState}, RMSError={RMSError}, ModelIndex={ModelIndex}, Coordinates={Coordinates}, MountRA={MountReportedRightAscension}, MountDEC={MountReportedDeclination}, MountLST={MountReportedLocalSiderealTime}, MountPier={MountReportedSideOfPier}, SolvedCoordinates={PlateSolvedCoordinates}, CaptureTime={CaptureTime}";
        }
    }
}