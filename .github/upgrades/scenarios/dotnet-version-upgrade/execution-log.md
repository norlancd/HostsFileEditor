
## [2026-05-29 13:38] 01-prerequisites

Updated global.json rollForward from latestFeature to latestMajor to allow .NET 10 SDK usage. Confirmed .NET 10 SDK installed and solution restores cleanly.


## [2026-05-29 13:54] 02-upgrade-all-projects

Upgraded all 4 projects to net10.0. Set up Central Package Management with Directory.Packages.props. Removed framework-included packages (System.Resources.Extensions, System.Text.Json, Microsoft.CSharp, System.Resources.ResourceManager), removed unused H.NotifyIcon.WinUI, updated Microsoft.Extensions.DependencyInjection to 10.0.8. Solution builds with 0 errors/warnings, all 63 tests pass.

