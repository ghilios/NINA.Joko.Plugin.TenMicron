﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Statistics.Distributions.Univariate;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugin.TenMicron.ModelManagement {

    public class ModelPointGenerator : IModelPointGenerator {
        public const int MAX_POINTS = 100;

        // Epsilon to optimize average nearest neighbor distance
        private const double EPSILON = 0.36d;

        private static readonly double GOLDEN_RATIO = (1.0d + Math.Sqrt(5d)) / 2.0d;

        private readonly IProfileService profileService;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly ITenMicronOptions options;
        private readonly IWeatherDataMediator weatherDataMediator;
        private readonly IMountMediator mountMediator;

        public ModelPointGenerator(IProfileService profileService, ITelescopeMediator telescopeMediator, IWeatherDataMediator weatherDataMediator, ITenMicronOptions options, IMountMediator mountMediator) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.weatherDataMediator = weatherDataMediator;
            this.options = options;
            this.mountMediator = mountMediator;
        }

        public List<ModelPoint> GenerateGoldenSpiral(int numPoints, CustomHorizon horizon) {
            if (numPoints > MAX_POINTS) {
                throw new Exception($"10u mounts do not support more than {MAX_POINTS} points");
            } else if (numPoints < 3) {
                throw new Exception("At least 3 points required for a viable model");
            }

            // http://extremelearning.com.au/how-to-evenly-distribute-points-on-a-sphere-more-effectively-than-the-canonical-fibonacci-lattice/
            var points = new List<ModelPoint>();

            int minViableNumPoints = 0;
            int maxViableNumPoints = int.MaxValue;
            int currentNumPoints = numPoints;
            while (true) {
                points.Clear();
                int validPoints = 0;
                for (int i = 0; i < currentNumPoints; ++i) {
                    // (azimuth) theta = 2 * pi * i / goldenRatio
                    // (altitude) phi = arccos(1 - 2 * (i + epsilon) / (n - 1 + 2 * epsilon))
                    var azimuth = Angle.ByRadians(2.0d * Math.PI * i / GOLDEN_RATIO);
                    // currentNumPoints * 2 enables us to process only half of the sphere
                    var inverseAltitude = Angle.ByRadians(Math.Acos(1.0d - 2.0d * ((double)i + EPSILON) / ((currentNumPoints * 2) - 1.0d + 2.0d * EPSILON)));
                    // The golden spiral algorithm uses theta from 0 - 180, where theta 0 is zenith
                    var altitudeDegrees = 90.0d - AstroUtil.EuclidianModulus(inverseAltitude.Degree, 180.0);
                    if (altitudeDegrees < 0.0d || double.IsNaN(altitudeDegrees)) {
                        continue;
                    }

                    var azimuthDegrees = AstroUtil.EuclidianModulus(azimuth.Degree, 360.0);
                    if (altitudeDegrees < 0.1d) {
                        altitudeDegrees = 0.1d;
                    }
                    if (altitudeDegrees > 89.9) {
                        altitudeDegrees = 89.9;
                    }

                    var horizonAltitude = horizon.GetAltitude(azimuthDegrees);
                    ModelPointStateEnum creationState;
                    if (altitudeDegrees < options.MinPointAltitude || altitudeDegrees > options.MaxPointAltitude) {
                        creationState = ModelPointStateEnum.OutsideAltitudeBounds;
                    } else if (azimuthDegrees < options.MinPointAzimuth || azimuthDegrees >= options.MaxPointAzimuth) {
                        creationState = ModelPointStateEnum.OutsideAzimuthBounds;
                    } else if (altitudeDegrees >= horizonAltitude) {
                        ++validPoints;
                        creationState = ModelPointStateEnum.Generated;
                    } else {
                        creationState = ModelPointStateEnum.BelowHorizon;
                    }
                    points.Add(
                        new ModelPoint(telescopeMediator) {
                            Altitude = altitudeDegrees,
                            Azimuth = azimuthDegrees,
                            ModelPointState = creationState
                        });
                }

                if (validPoints == numPoints) {
                    return points;
                } else if (validPoints < numPoints) {
                    // After excluding points below the horizon, we are short. Remember where we currently are, and try more points in another iteration.
                    // This may take several iterations, but it is guaranteed to converge
                    minViableNumPoints = currentNumPoints;
                    var nextNumPoints = Math.Min(maxViableNumPoints, currentNumPoints + (numPoints - validPoints));
                    if (nextNumPoints == currentNumPoints) {
                        if (validPoints < numPoints) {
                            Notification.ShowInformation($"Only {validPoints} could be generated. Continuing");
                            Logger.Warning($"Only {validPoints} could be generated. Continuing");
                        }
                        return points;
                    }
                    currentNumPoints = nextNumPoints;
                } else {
                    // After excluding points below the horizon, we still have too many.
                    maxViableNumPoints = currentNumPoints - 1;
                    var nextNumPoints = Math.Max(minViableNumPoints + 1, currentNumPoints - (validPoints - numPoints));
                    if (nextNumPoints == currentNumPoints) {
                        // Next run will be the last
                        currentNumPoints = nextNumPoints - 1;
                    } else {
                        currentNumPoints = nextNumPoints;
                    }
                }
            }
        }

        private TopocentricCoordinates ToTopocentric(Coordinates coordinates, DateTime dateTime) {
            var coordinatesAtTime = new Coordinates(Angle.ByHours(coordinates.RA), Angle.ByDegree(coordinates.Dec), coordinates.Epoch, new ConstantDateTime(dateTime));
            var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
            var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
            var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;

            var weatherDataInfo = weatherDataMediator.GetInfo();
            var pressurehPa = weatherDataInfo.Connected ? weatherDataInfo.Pressure : 0.0d;
            var temperature = weatherDataInfo.Connected ? weatherDataInfo.Temperature : 0.0d;
            var wavelength = weatherDataInfo.Connected ? 0.55d : 0.0d;
            var humidity = weatherDataInfo.Connected && !double.IsNaN(weatherDataInfo.Humidity) ? weatherDataInfo.Humidity : 0.0d;
            return coordinatesAtTime.Transform(
                latitude: latitude,
                longitude: longitude,
                elevation: elevation,
                pressurehPa: pressurehPa,
                tempCelcius: temperature,
                relativeHumidity: humidity,
                wavelength: wavelength);
        }

        public List<ModelPoint> GenerateSiderealPath(Coordinates coordinates, Angle raDelta, DateTime startTime, DateTime endTime, CustomHorizon horizon) {
            if (endTime < startTime) {
                throw new Exception($"End time ({endTime}) comes before start time ({startTime})");
            }
            if (endTime > (startTime + TimeSpan.FromDays(1))) {
                throw new Exception($"End time ({endTime}) is more than 1 day beyond start time ({startTime})");
            }
            if (TimeSpan.FromHours(raDelta.Hours) <= TimeSpan.FromSeconds(1)) {
                throw new Exception($"RA delta ({raDelta}) cannot be less than 1 arc second");
            }

            var meridianLimitDegrees = mountMediator.GetInfo().MeridianLimitDegrees;
            Logger.Info($"Using meridian limit {meridianLimitDegrees:0.##}°");
            var meridianUpperLimit = meridianLimitDegrees + 0.1d;
            var meridianLowerLimit = 360.0d - meridianLimitDegrees - 0.1d;
            var points = new List<ModelPoint>();
            var decJitterSigmaDegrees = this.options.DecJitterSigmaDegrees;
            while (true) {
                points.Clear();
                int validPoints = 0;

                var currentTime = startTime;
                var raDeltaTime = TimeSpan.FromHours(raDelta.Hours);
                while (currentTime < endTime) {
                    var nextCoordinates = coordinates.Clone();

                    var decJitter = NormalDistribution.Random(mean: 0.0, stdDev: decJitterSigmaDegrees);
                    decJitter = Math.Min(3.0d * decJitterSigmaDegrees, Math.Max(-3.0d * decJitterSigmaDegrees, decJitter));

                    var nextDec = nextCoordinates.Dec + decJitter;
                    nextCoordinates.Dec = Math.Min(90.0d, Math.Max(-90.0d, nextDec));

                    var pointCoordinates = ToTopocentric(nextCoordinates, currentTime);
                    var azimuthDegrees = pointCoordinates.Azimuth.Degree;
                    var altitudeDegrees = pointCoordinates.Altitude.Degree;

                    // Make sure no point is within the meridian limits
                    if (azimuthDegrees < meridianUpperLimit) {
                        Logger.Info($"Point Alt={altitudeDegrees:0.##}, Az={azimuthDegrees:0.##} within meridian limit. Adjusting azimuth to {meridianUpperLimit:0.##}");
                        azimuthDegrees = meridianUpperLimit;
                    }
                    if (azimuthDegrees > meridianLowerLimit) {
                        Logger.Info($"Point Alt={altitudeDegrees:0.##}, Az={azimuthDegrees:0.##} within meridian limit. Adjusting azimuth to {meridianLowerLimit:0.##}");
                        azimuthDegrees = meridianLowerLimit;
                    }

                    var horizonAltitude = horizon.GetAltitude(azimuthDegrees);
                    ModelPointStateEnum creationState;
                    if (altitudeDegrees < options.MinPointAltitude || altitudeDegrees > options.MaxPointAltitude) {
                        creationState = ModelPointStateEnum.OutsideAltitudeBounds;
                    } else if (azimuthDegrees < options.MinPointAzimuth || azimuthDegrees >= options.MaxPointAzimuth) {
                        creationState = ModelPointStateEnum.OutsideAzimuthBounds;
                    } else if (altitudeDegrees >= horizonAltitude) {
                        ++validPoints;
                        creationState = ModelPointStateEnum.Generated;
                    } else {
                        creationState = ModelPointStateEnum.BelowHorizon;
                    }

                    points.Add(
                        new ModelPoint(telescopeMediator) {
                            Altitude = altitudeDegrees,
                            Azimuth = azimuthDegrees,
                            ModelPointState = creationState
                        });
                    currentTime += raDeltaTime;
                }

                if (validPoints <= MAX_POINTS) {
                    return points;
                } else {
                    // We have too many points, so increase until we're below the limit. This algorithm is guaranteed to converge as we always increase the ra delta
                    var increaseRatio = (double)validPoints / MAX_POINTS;
                    var nextRaDelta = Angle.ByDegree(raDelta.Degree * increaseRatio);
                    Logger.Info($"Too many points ({validPoints}) generated with RA delta ({raDelta}). Increasing to {nextRaDelta}");

                    raDelta = nextRaDelta;
                }
            }
        }
    }
}