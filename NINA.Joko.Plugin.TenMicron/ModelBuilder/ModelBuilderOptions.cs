#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace NINA.Joko.Plugin.TenMicron.ModelBuilder {

    public class ModelBuilderOptions : BaseINPC, IModelBuilderOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public ModelBuilderOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(ModelBuilderOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            InitializeOptions();
        }

        private void InitializeOptions() {
            goldenSpiralStarCount = optionsAccessor.GetValueInt32("GoldenSpiralStarCount", 9);
            siderealTrackStartOffsetSeconds = optionsAccessor.GetValueInt32("SiderealTrackStartOffsetSeconds", 0);
            siderealTrackEndOffsetSeconds = optionsAccessor.GetValueInt32("SiderealTrackEndOffsetSeconds", 0);
            siderealTrackRADeltaDegrees = optionsAccessor.GetValueDouble("SiderealTrackRADeltaDegrees", 1.5d);
            domeShutterWidth_mm = optionsAccessor.GetValueInt32("DomeShutterWidth_mm", 0);
            minimizeDomeMovementEnabled = optionsAccessor.GetValueBoolean("MinimizeDomeMovementEnabled", true);
            modelPointGenerationType = optionsAccessor.GetValueEnum("ModelPointGenerationType", ModelPointGenerationTypeEnum.GoldenSpiral);
        }

        public void ResetDefaults() {
            GoldenSpiralStarCount = 9;
            SiderealTrackStartOffsetSeconds = 0;
            SiderealTrackEndOffsetSeconds = 0;
            SiderealTrackRADeltaDegrees = 1.5d;
            DomeShutterWidth_mm = 0;
            MinimizeDomeMovementEnabled = true;
            ModelPointGenerationType = ModelPointGenerationTypeEnum.GoldenSpiral;
        }

        private int goldenSpiralStarCount;

        public int GoldenSpiralStarCount {
            get => goldenSpiralStarCount;
            set {
                if (goldenSpiralStarCount != value) {
                    if (value < 3 || value > 90) {
                        throw new ArgumentException("GoldenSpiralStarCount must be between 3 and 90, inclusive", "GoldenSpiralStarCount");
                    }
                    goldenSpiralStarCount = value;
                    optionsAccessor.SetValueInt32("GoldenSpiralStarCount", goldenSpiralStarCount);
                    RaisePropertyChanged();
                }
            }
        }

        private int siderealTrackStartOffsetSeconds;

        public int SiderealTrackStartOffsetSeconds {
            get => siderealTrackStartOffsetSeconds;
            set {
                if (siderealTrackStartOffsetSeconds != value) {
                    siderealTrackStartOffsetSeconds = value;
                    optionsAccessor.SetValueInt32("SiderealTrackStartOffsetSeconds", siderealTrackStartOffsetSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private int siderealTrackEndOffsetSeconds;

        public int SiderealTrackEndOffsetSeconds {
            get => siderealTrackEndOffsetSeconds;
            set {
                if (siderealTrackEndOffsetSeconds != value) {
                    siderealTrackEndOffsetSeconds = value;
                    optionsAccessor.SetValueInt32("SiderealTrackEndOffsetSeconds", siderealTrackEndOffsetSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private double siderealTrackRADeltaDegrees;

        public double SiderealTrackRADeltaDegrees {
            get => siderealTrackRADeltaDegrees;
            set {
                if (siderealTrackRADeltaDegrees != value) {
                    if (value <= 0.0d) {
                        throw new ArgumentException("SiderealTrackRADeltaDegrees must be positive", "SiderealTrackRADeltaDegrees");
                    }
                    siderealTrackRADeltaDegrees = value;
                    optionsAccessor.SetValueDouble("SiderealTrackRADeltaDegrees", siderealTrackRADeltaDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private int domeShutterWidth_mm;

        public int DomeShutterWidth_mm {
            get => domeShutterWidth_mm;
            set {
                if (domeShutterWidth_mm != value) {
                    if (value <= 0) {
                        throw new ArgumentException("DomeShutterWidth_mm must be non-negative", "DomeShutterWidth_mm");
                    }
                    domeShutterWidth_mm = value;
                    optionsAccessor.SetValueInt32("DomeShutterWidth_mm", domeShutterWidth_mm);
                    RaisePropertyChanged();
                }
            }
        }

        private bool minimizeDomeMovementEnabled;

        public bool MinimizeDomeMovementEnabled {
            get => minimizeDomeMovementEnabled;
            set {
                if (minimizeDomeMovementEnabled != value) {
                    minimizeDomeMovementEnabled = value;
                    optionsAccessor.SetValueBoolean("MinimizeDomeMovementEnabled", minimizeDomeMovementEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private ModelPointGenerationTypeEnum modelPointGenerationType;

        public ModelPointGenerationTypeEnum ModelPointGenerationType {
            get => modelPointGenerationType;
            set {
                if (modelPointGenerationType != value) {
                    modelPointGenerationType = value;
                    optionsAccessor.SetValueEnum("ModelPointGenerationType", modelPointGenerationType);
                    RaisePropertyChanged();
                }
            }
        }
    }
}