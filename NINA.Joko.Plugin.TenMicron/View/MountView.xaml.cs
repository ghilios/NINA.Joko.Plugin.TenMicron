#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Joko.Plugin.TenMicron.View {

    /// <summary>
    /// Interaction logic for MountView.xaml
    /// </summary>
    public partial class MountView : UserControl {

        public MountView() {
            InitializeComponent();
        }

        private void UnattendedFlipEnabled_Unchecked(object sender, RoutedEventArgs e) {
            var checkBox = (CheckBox)sender;
            var mountVM = (MountVM)checkBox.DataContext;
            mountVM.DisableUnattendedFlip();
        }

        private void DualAxisTrackingEnabled_Toggled(object sender, RoutedEventArgs e) {
            var checkBox = (CheckBox)sender;
            var mountVM = (MountVM)checkBox.DataContext;
            var isChecked = checkBox.IsChecked;
            // Checked/Unchecked fire before the binding writes back to MountInfo, so a match means the checkbox moved
            // because a poll updated MountInfo, not because the user clicked it. Don't echo that back to the mount
            if (isChecked.HasValue && isChecked.Value != mountVM.MountInfo.DualAxisTrackingEnabled) {
                mountVM.SetDualAxisTracking(isChecked.Value);
            }
        }

        private void RefractionCorrectionEnabled_Toggled(object sender, RoutedEventArgs e) {
            var checkBox = (CheckBox)sender;
            var mountVM = (MountVM)checkBox.DataContext;
            var isChecked = checkBox.IsChecked;
            // See DualAxisTrackingEnabled_Toggled
            if (isChecked.HasValue && isChecked.Value != mountVM.MountInfo.RefractionCorrectionEnabled) {
                mountVM.SetRefactionCorrectionEnabled(isChecked.Value);
            }
        }
    }
}