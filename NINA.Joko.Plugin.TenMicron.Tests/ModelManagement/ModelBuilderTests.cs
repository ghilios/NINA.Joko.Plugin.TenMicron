using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyDome;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.ModelManagement;
using NINA.Joko.Plugin.TenMicron.Tests.TestHelpers;
using NINA.PlateSolving.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.ModelManagement {

    // ModelBuilder is 959 LOC with 11 mediator deps. This fixture covers the failure-mode
    // entrypoint (PreFlightChecks) plus the lifecycle invariants for state snapshot/restore
    // (refraction, dome follower, dual-axis tracking, exception cleanup). The full alignment
    // loop is not exercised — these tests drive Build() into DoBuild and either let it short-
    // circuit via a pre-cancelled stopToken, or trigger an early throw from DoBuild's first
    // mediator calls.
    [TestFixture]
    public class ModelBuilderTests {

        private static ModelBuilder BuildSut(
            Mock<ITelescopeMediator> telescope = null,
            Mock<ICameraMediator> camera = null) {
            telescope ??= new Mock<ITelescopeMediator>();
            camera ??= new Mock<ICameraMediator>();

            return new ModelBuilder(
                profileService: MockProfileBuilder.Build().Object,
                mountModelMediator: new Mock<IMountModelMediator>().Object,
                mount: new Mock<IMount>().Object,
                telescopeMediator: telescope.Object,
                domeMediator: new Mock<IDomeMediator>().Object,
                cameraMediator: camera.Object,
                domeSynchronization: new Mock<IDomeSynchronization>().Object,
                plateSolverFactory: new Mock<IPlateSolverFactory>().Object,
                imagingMediator: new Mock<IImagingMediator>().Object,
                filterWheelMediator: new Mock<IFilterWheelMediator>().Object,
                weatherDataMediator: new Mock<IWeatherDataMediator>().Object);
        }

        [Test]
        public async Task Build_TelescopeNotConnected_ThrowsPreflightCheck() {
            var telescope = new Mock<ITelescopeMediator>();
            telescope.Setup(t => t.GetInfo()).Returns(new TelescopeInfo { Connected = false });
            var sut = BuildSut(telescope: telescope);

            Func<Task> act = () => sut.Build(new List<ModelPoint>(), new ModelBuilderOptions());

            await act.Should().ThrowAsync<Exception>().WithMessage("No telescope connected");
        }

        [Test]
        public async Task Build_CameraNotConnected_ThrowsPreflightCheck() {
            var telescope = new Mock<ITelescopeMediator>();
            telescope.Setup(t => t.GetInfo()).Returns(new TelescopeInfo { Connected = true });
            var camera = new Mock<ICameraMediator>();
            camera.Setup(c => c.GetInfo()).Returns(new CameraInfo { Connected = false });
            var sut = BuildSut(telescope: telescope, camera: camera);

            Func<Task> act = () => sut.Build(new List<ModelPoint>(), new ModelBuilderOptions());

            await act.Should().ThrowAsync<Exception>().WithMessage("No camera connected");
        }

        // Pre-cancel stopToken so DoBuild's first stopOrCancelCt.ThrowIfCancellationRequested()
        // is caught and turned into a clean exit; lets us verify the finally-block restoration
        // without driving the full alignment loop.
        private static (CancellationToken ct, CancellationToken stopToken) PreCancelledStopToken() {
            var stopCts = new CancellationTokenSource();
            stopCts.Cancel();
            return (CancellationToken.None, stopCts.Token);
        }

        [Test]
        public async Task Build_RefractionDisabled_RestoredOnCompletion() {
            var env = new MockModelBuilderEnvironment();
            env.EnableRefractionTracking();
            var (ct, stopToken) = PreCancelledStopToken();
            var sut = env.Build();

            await sut.Build(new List<ModelPoint>(), new ModelBuilderOptions { DisableRefractionCorrection = true }, ct, stopToken);

            env.Mount.Verify(m => m.SetRefractionCorrection(false), Times.Once);
            env.Mount.Verify(m => m.SetRefractionCorrection(true), Times.Once);
        }

        [Test]
        public async Task Build_RefractionDisabled_RestoredOnException() {
            var env = new MockModelBuilderEnvironment();
            env.EnableRefractionTracking();
            // Trip DoBuild early: StartNewAlignmentSpec returning false throws ModelBuildException
            // before ProcessPoints. Exception propagates to Build's finally.
            env.MountModelMediator.Setup(m => m.StartNewAlignmentSpec()).Returns(false);
            var sut = env.Build();

            Func<Task> act = () => sut.Build(new List<ModelPoint>(), new ModelBuilderOptions { DisableRefractionCorrection = true });

            await act.Should().ThrowAsync<Exception>().WithMessage("Failed to start new alignment spec");
            env.Mount.Verify(m => m.SetRefractionCorrection(false), Times.Once);
            env.Mount.Verify(m => m.SetRefractionCorrection(true), Times.Once);
        }

        [Test]
        public async Task Build_DomeFollowerDisabled_RestoredOnCompletion() {
            // UseDome=true (set below via Dome.GetInfo) activates PreStep3 inside DoBuild, which
            // calls AstroUtil.GetLocalSiderealTimeNow → NOVAS native lib. CI runners without a
            // local NINA install can't satisfy that, so gate this test on the NINA assets.
            NinaAssetGate.RequireNina();

            var env = new MockModelBuilderEnvironment();
            // Connected dome with settable azimuth -> state.UseDome = true.
            env.Dome.Setup(d => d.GetInfo()).Returns(new DomeInfo { Connected = true, CanSetAzimuth = true });
            env.Dome.SetupGet(d => d.IsFollowingScope).Returns(true);
            env.Dome.Setup(d => d.DisableFollowing(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            env.Dome.Setup(d => d.EnableFollowing(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var (ct, stopToken) = PreCancelledStopToken();
            var sut = env.Build();

            await sut.Build(new List<ModelPoint>(), new ModelBuilderOptions(), ct, stopToken);

            env.Dome.Verify(d => d.DisableFollowing(It.IsAny<CancellationToken>()), Times.Once);
            env.Dome.Verify(d => d.EnableFollowing(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Build_DATDisabled_RestoredOnCompletion() {
            var env = new MockModelBuilderEnvironment();
            env.Mount.Setup(m => m.GetDualAxisTrackingEnabled()).Returns(new Response<bool>(true, ""));
            env.Mount.Setup(m => m.SetDualAxisTracking(false)).Returns(new Response<bool>(true, ""));
            env.Mount.Setup(m => m.SetDualAxisTracking(true)).Returns(new Response<bool>(true, ""));
            var (ct, stopToken) = PreCancelledStopToken();
            var sut = env.Build();

            await sut.Build(new List<ModelPoint>(), new ModelBuilderOptions { DisableDATAlignment = true }, ct, stopToken);

            env.Mount.Verify(m => m.SetDualAxisTracking(false), Times.Once);
            env.Mount.Verify(m => m.SetDualAxisTracking(true), Times.Once);
        }

        [Test]
        public async Task Build_DownstreamThrows_CleanupRuns() {
            // PreFlightChecks must pass (Camera.Connected = true from default), then we trip
            // DoBuild's first MountModel call via DeleteAlignment throwing — simulating a
            // downstream mediator failure after the snapshot/disable phase. Refraction was
            // disabled up-front, and the test asserts the finally restores it regardless of
            // which downstream call faulted.
            var env = new MockModelBuilderEnvironment();
            env.EnableRefractionTracking();
            var downstreamFailure = new InvalidOperationException("alignment delete failed");
            env.MountModelMediator.Setup(m => m.DeleteAlignment()).Throws(downstreamFailure);
            var sut = env.Build();

            Func<Task> act = () => sut.Build(new List<ModelPoint>(), new ModelBuilderOptions { DisableRefractionCorrection = true });

            // The exception should propagate, but refraction must still be re-enabled by the finally.
            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("alignment delete failed");
            env.Mount.Verify(m => m.SetRefractionCorrection(false), Times.Once);
            env.Mount.Verify(m => m.SetRefractionCorrection(true), Times.Once);
        }
    }
}
