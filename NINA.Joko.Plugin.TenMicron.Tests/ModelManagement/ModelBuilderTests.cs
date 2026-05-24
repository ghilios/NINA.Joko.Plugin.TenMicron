using System;
using System.Collections.Generic;
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
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Model;
using NINA.Joko.Plugin.TenMicron.ModelManagement;
using NINA.Joko.Plugin.TenMicron.Tests.TestHelpers;
using NINA.PlateSolving.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugin.TenMicron.Tests.ModelManagement {

    // Full coverage of ModelBuilder (959 LOC, 11 mediator deps) is high-effort. This fixture ships
    // one real scenario that exercises the failure-mode entrypoint, plus named-but-ignored stubs the
    // next contributor can fill in by following the same wiring pattern.
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

            await act.Should().ThrowAsync<System.Exception>().WithMessage("No telescope connected");
        }

        [Test]
        public async Task Build_CameraNotConnected_ThrowsPreflightCheck() {
            var telescope = new Mock<ITelescopeMediator>();
            telescope.Setup(t => t.GetInfo()).Returns(new TelescopeInfo { Connected = true });
            var camera = new Mock<ICameraMediator>();
            camera.Setup(c => c.GetInfo()).Returns(new CameraInfo { Connected = false });
            var sut = BuildSut(telescope: telescope, camera: camera);

            Func<Task> act = () => sut.Build(new List<ModelPoint>(), new ModelBuilderOptions());

            await act.Should().ThrowAsync<System.Exception>().WithMessage("No camera connected");
        }

        [Test, Ignore("Scaffold — wires 8+ mediators; implement when refraction restoration coverage is needed.")]
        public Task Build_RefractionDisabled_RestoredOnCompletion() => Task.CompletedTask;

        [Test, Ignore("Scaffold — implement to assert refraction is restored even when DoBuild throws.")]
        public Task Build_RefractionDisabled_RestoredOnException() => Task.CompletedTask;

        [Test, Ignore("Scaffold — implement to assert dome follower re-enabled after build.")]
        public Task Build_DomeFollowerDisabled_RestoredOnCompletion() => Task.CompletedTask;

        [Test, Ignore("Scaffold — implement for commit b313777 DAT-disable lifecycle.")]
        public Task Build_DATDisabled_RestoredOnCompletion() => Task.CompletedTask;

        [Test, Ignore("Scaffold — implement to assert cleanup runs when ICameraMediator.Capture throws.")]
        public Task Build_CameraThrows_CleanupRuns() => Task.CompletedTask;
    }
}
