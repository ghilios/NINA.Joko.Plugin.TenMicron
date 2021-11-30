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
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugin.TenMicron.ModelBuilder {

    public class ModelPointGenerator : IModelPointGenerator {

        // Epsilon to optimize average nearest neighbor distance
        private const double EPSILON = 0.36d;

        private static readonly double GOLDEN_RATIO = (1.0d + Math.Sqrt(5d)) / 2.0d;

        private readonly ICustomDateTime dateTime;
        private readonly ITelescopeMediator telescopeMediator;

        public ModelPointGenerator(ICustomDateTime dateTime, ITelescopeMediator telescopeMediator) {
            this.dateTime = dateTime;
            this.telescopeMediator = telescopeMediator;
        }

        public List<ModelPoint> GenerateGoldenSpiral(int numPoints, CustomHorizon horizon) {
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
                    var altitude = Angle.ByRadians(Math.Acos(1.0d - 2.0d * ((double)i + EPSILON) / ((currentNumPoints * 2) - 1.0d + 2.0d * EPSILON)));
                    // The golden spiral algorithm uses theta from 0 - 180, where theta 0 is zenith
                    var altitudeDegrees = 90.0d - AstroUtil.EuclidianModulus(altitude.Degree, 180.0);
                    if (altitudeDegrees < 0.0 || double.IsNaN(altitudeDegrees)) {
                        continue;
                    }

                    var azimuthDegrees = AstroUtil.EuclidianModulus(azimuth.Degree, 360.0);
                    var horizonAltitude = horizon.GetAltitude(azimuthDegrees);
                    ModelPointStateEnum creationState;
                    if (altitude.Degree >= horizonAltitude) {
                        ++validPoints;
                        creationState = ModelPointStateEnum.Generated;
                    } else {
                        creationState = ModelPointStateEnum.BelowHorizon;
                    }
                    points.Add(
                        new ModelPoint(dateTime, telescopeMediator) {
                            Altitude = altitude.Degree,
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
    }
}