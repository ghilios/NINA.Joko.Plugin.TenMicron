using FluentAssertions;
using NINA.Joko.Plugin.TenMicron.Extensions.OxyPlot;
using NINA.Joko.Plugin.TenMicron.Model;
using NUnit.Framework;
using OxyPlot;
using System;

namespace NINA.Joko.Plugin.TenMicron.Tests.Extensions {

    [TestFixture]
    public class ModelPointStateColorAxisTests {

        [Test]
        public void GetColor_CloseToMeridian_MatchesOtherExcludedStates() {
            // Regression: CloseToMeridian had no explicit color mapping and fell through to the
            // black fallback, rendering an unexplained black dot on the model builder chart.
            var axis = new ModelPointStateColorAxis();
            axis.GetColor((int)ModelPointStateEnum.CloseToMeridian).Should().Be(OxyColors.Brown);
        }

        [Test]
        public void GetColor_EveryDefinedState_HasExplicitColor() {
            // Black is the "unmapped state" fallback; no defined enum value should hit it.
            var axis = new ModelPointStateColorAxis();
            foreach (var state in Enum.GetValues<ModelPointStateEnum>()) {
                axis.GetColor((int)state).Should().NotBe(OxyColors.Black, because: $"{state} should have an explicit chart color");
            }
        }
    }
}
