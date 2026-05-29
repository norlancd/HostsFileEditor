# 02-upgrade-all-projects: Upgrade all projects to net10.0

Update all four projects to target net10.0 simultaneously. This is the core upgrade task:

- **HostsFileEditor.Core** (`net9.0-windows` → `net10.0-windows`): 1 source-incompatible API, 1 NuGet package upgrade (`Microsoft.Extensions.DependencyInjection` 9→10). Small library with no breaking-change risk beyond the flagged items.
- **HostsFileEditor.Core.Tests** (`net9.0-windows` → `net10.0-windows`): TFM change only. Update `System.Resources.Extensions` and `System.Text.Json` to their net10.0 versions.
- **HostsFileEditor.WinForm** (`net9.0-windows` → `net10.0-windows`): Most complex project. Assessment flagged 2,589 binary-incompatible WinForms APIs (type-forwarding/platform changes), 181 source-incompatible APIs, Legacy Configuration usage (17 issues), Windows Forms Legacy Controls (350 issues), GDI+/System.Drawing usage (164 issues). Package `Equin.ApplicationFramework.BindingListView` is incompatible and has no known replacement — requires inline research. `System.Resources.ResourceManager` is now framework-included and should be removed. Start by investigating the WinForms binary-incompatible count: many of these are type-forwarder issues that resolve automatically with a TFM bump.
- **HostsFileEditor.WinUI** (`net9.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0`): `H.NotifyIcon.WinUI` is incompatible — research a compatible version or alternative. 1 source-incompatible API. Upgrade `Microsoft.Extensions.DependencyInjection` to v10.

Also set up Central Package Management (CPM) as part of this task: create `Directory.Packages.props`, move all version attributes out of project files, and resolve the `System.Resources.Extensions` version divergence (currently at both 9.0.8 and 9.0.9 across projects).

Operation sequence: update all TFMs → set up CPM and move package versions → restore → build solution → fix all compilation errors in a single bounded pass.

**Done when**: All 4 projects target net10.0 (or net10.0-windows); solution builds with 0 errors and 0 warnings; `Directory.Packages.props` exists with all package versions centralized; incompatible packages resolved or removed; all tests pass.

---

## Research Findings

### Package decisions
- `System.Resources.Extensions` — framework-included on net10.0; removed from all projects
- `System.Text.Json` — framework-included on net10.0; removed from WinUI
- `Microsoft.CSharp` — framework-included on net10.0; removed from WinForm
- `System.Resources.ResourceManager` — framework-included (was flagged NuGet.0003); removed from WinForm
- `H.NotifyIcon.WinUI` — no usage found in any .cs or .xaml source files; removed
- `Equin.ApplicationFramework.BindingListView` — used in 4 places in MainForm.cs; kept (builds fine on net10.0 despite incompatibility flag)
- `Microsoft.Extensions.DependencyInjection` — bumped to 10.0.8 in CPM
- All other packages — kept at existing versions (compatible)

### API issues (post-build verification)
- `System.String.Format` / `System.String.Join` span overloads flagged in Core — false positives, builds cleanly
- `TimeSpan.FromMilliseconds` in WinUI AnimationService — false positive, builds cleanly  
- WinUI `MicaController` and `Grid.Padding` issues from VS build — false positives from stale VS IntelliSense; `dotnet build` confirms 0 errors

### CPM setup
- Created `Directory.Packages.props` at solution root
- All `Version` attributes removed from `PackageReference` elements across all 4 projects
