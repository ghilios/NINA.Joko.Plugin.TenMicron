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
using NINA.Joko.Plugin.TenMicron.ModelManagement;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;

namespace NINA.Joko.Plugin.TenMicron.Tests.TestHelpers {

    // Wires the 11 mediator dependencies ModelBuilder takes plus the minimum default
    // state needed for Build(...) to pass PreFlightChecks and enter DoBuild. Tests reach
    // into the exposed mocks to layer their scenario-specific Setup(...) calls.
    public class MockModelBuilderEnvironment {
        public Mock<IProfileService> ProfileService { get; } = new();
        public Mock<IMountModelMediator> MountModelMediator { get; } = new();
        public Mock<IMount> Mount { get; } = new();
        public Mock<ITelescopeMediator> Telescope { get; } = new();
        public Mock<IDomeMediator> Dome { get; } = new();
        public Mock<ICameraMediator> Camera { get; } = new();
        public Mock<IDomeSynchronization> DomeSynchronization { get; } = new();
        public Mock<IPlateSolverFactory> PlateSolverFactory { get; } = new();
        public Mock<IImagingMediator> Imaging { get; } = new();
        public Mock<IFilterWheelMediator> FilterWheel { get; } = new();
        public Mock<IWeatherDataMediator> WeatherData { get; } = new();

        public MockModelBuilderEnvironment() {
            // Profile chain: AstrometrySettings + DomeSettings present so PreStep3 + DoBuild access don't NRE.
            var astrometry = new Mock<IAstrometrySettings>();
            astrometry.SetupGet(x => x.Latitude).Returns(40.0);
            astrometry.SetupGet(x => x.Longitude).Returns(-74.0);
            astrometry.SetupGet(x => x.Elevation).Returns(100.0);
            var domeSettings = new Mock<IDomeSettings>();
            domeSettings.SetupProperty(x => x.SyncSlewDomeWhenMountSlews, false);
            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometry.Object);
            profile.SetupGet(x => x.DomeSettings).Returns(domeSettings.Object);
            ProfileService.SetupGet(x => x.ActiveProfile).Returns(profile.Object);

            // PreFlightChecks needs Connected=true on both telescope and camera.
            Telescope.Setup(t => t.GetInfo()).Returns(new TelescopeInfo { Connected = true });
            Camera.Setup(c => c.GetInfo()).Returns(new CameraInfo { Connected = true });

            // Dome: not connected so state.UseDome = false (skips PreStep3 + dome-follower branch).
            Dome.Setup(d => d.GetInfo()).Returns((DomeInfo)null);
            // IsFollowingScope defaults to false; explicit for readability.
            Dome.SetupGet(d => d.IsFollowingScope).Returns(false);

            // Refraction disabled by default -> state ctor skips weather lookup. Tests that
            // flip refraction on can rely on the default weather-data info below.
            Mount.Setup(m => m.GetRefractionCorrectionEnabled()).Returns(new Response<bool>(false, ""));
            Mount.Setup(m => m.GetPressure()).Returns(new Response<decimal>(0m, ""));
            Mount.Setup(m => m.GetTemperature()).Returns(new Response<decimal>(0m, ""));
            Mount.Setup(m => m.GetDualAxisTrackingEnabled()).Returns(new Response<bool>(false, ""));
            // Weather data: Connected=false short-circuits the humidity branch when refraction is on.
            WeatherData.Setup(w => w.GetInfo()).Returns(new WeatherDataInfo { Connected = false });

            // Filter wheel returns null GetInfo -> oldFilter = null, no restore branch.
            FilterWheel.Setup(f => f.GetInfo()).Returns((FilterWheelInfo)null);

            // MountModel must return true from StartNewAlignmentSpec or DoBuild throws ModelBuildException.
            MountModelMediator.Setup(m => m.StartNewAlignmentSpec()).Returns(true);
            MountModelMediator.Setup(m => m.FinishAlignmentSpec()).Returns(true);
        }

        // Flips the default refraction-off state to on, plus wires the disable/enable
        // round-trip so tests asserting the snapshot-and-restore invariant don't have to
        // repeat the same three Setup(...) calls.
        public void EnableRefractionTracking() {
            Mount.Setup(m => m.GetRefractionCorrectionEnabled()).Returns(new Response<bool>(true, ""));
            Mount.Setup(m => m.SetRefractionCorrection(false)).Returns(new Response<bool>(true, ""));
            Mount.Setup(m => m.SetRefractionCorrection(true)).Returns(new Response<bool>(true, ""));
        }

        public ModelBuilder Build() => new ModelBuilder(
            profileService: ProfileService.Object,
            mountModelMediator: MountModelMediator.Object,
            mount: Mount.Object,
            telescopeMediator: Telescope.Object,
            domeMediator: Dome.Object,
            cameraMediator: Camera.Object,
            domeSynchronization: DomeSynchronization.Object,
            plateSolverFactory: PlateSolverFactory.Object,
            imagingMediator: Imaging.Object,
            filterWheelMediator: FilterWheel.Object,
            weatherDataMediator: WeatherData.Object);
    }
}
