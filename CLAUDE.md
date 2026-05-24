# Project layout

This solution has three C# projects. They have similar names — be careful which one a request refers to.

| Project | Type | What it is |
|---|---|---|
| `NINA.Joko.Plugin.TenMicron/` | WPF class library | The plugin itself. Hosted by NINA. |
| `NINA.Joko.Plugin.TenMicron.Tests/` | NUnit test project | The unit-test suite (Moq + FluentAssertions). |
| `TestApp/` | WPF executable | A standalone harness app that exercises the plugin against a real mount/ASCOM. Not a test project. |

When the user says:

- **"TestApp"** → they mean `TestApp/TestApp.csproj`. The standalone WPF exe.
- **"test project" / "tests" / "unit tests" / "the test suite"** → they mean `NINA.Joko.Plugin.TenMicron.Tests/`.
- **"the plugin"** → `NINA.Joko.Plugin.TenMicron/`.

When upgrading NINA.Plugin or any package the plugin csproj brings in transitively, both `TestApp/TestApp.csproj` and the plugin csproj usually need bumping together — `TestApp` has its own direct `<PackageReference Include="NINA.Plugin" ... />` which NuGet treats as a separate constraint.
