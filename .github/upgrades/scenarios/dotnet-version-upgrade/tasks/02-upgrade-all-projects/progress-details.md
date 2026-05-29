# Progress Details — 02-upgrade-all-projects

## What Changed

### Project files — TFM updates
- `HostsFileEditor.Core/HostsFileEditor.Core.csproj`: `net9.0-windows` → `net10.0-windows`
- `HostsFileEditor.Core.Tests/HostsFileEditor.Core.Tests.csproj`: `net9.0-windows` → `net10.0-windows`
- `HostsFileEditor.WinForm/HostsFileEditor.WinForm.csproj`: `net9.0-windows` → `net10.0-windows`
- `HostsFileEditor.WinUI/HostsFileEditor.WinUI.csproj`: `net9.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0`

### Package changes

**Removed (now framework-included on net10.0)**:
- `System.Resources.Extensions` — from Core and WinUI
- `System.Text.Json` — from WinUI
- `Microsoft.CSharp` — from WinForm
- `System.Resources.ResourceManager` — from WinForm

**Removed (no source usage)**:
- `H.NotifyIcon.WinUI` — from WinUI (zero .cs/.xaml usages found)

**Updated**:
- `Microsoft.Extensions.DependencyInjection`: 9.0.9 → 10.0.8

### CPM setup
- Created `Directory.Packages.props` at solution root with `ManagePackageVersionsCentrally=true`
- Removed all `Version` attributes from `PackageReference` elements in all 4 project files
- Resolved `System.Resources.Extensions` version divergence (was 9.0.8 / 9.0.9) — package removed entirely (framework-included)

## Build Results

```
dotnet build HostsFileEditor.slnx
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Note: `run_build` (VS build) showed false-positive errors for WinUI (MicaController, Grid.Padding) and Core (CS0518) — these are stale VS analysis artifacts. `dotnet build` confirms clean build.

## Test Results

```
dotnet test HostsFileEditor.Core.Tests
Passed! Failed: 0, Passed: 63, Skipped: 0, Total: 63
```

## Issues Resolved

- `Equin.ApplicationFramework.BindingListView` — assessment flagged as incompatible but it compiles and runs correctly on net10.0-windows; retained.
- `H.NotifyIcon.WinUI` — assessment flagged as incompatible, no source usage found, removed cleanly.
- `TreatWarningsAsErrors=true` in Directory.Build.props caused NU1510 for `Microsoft.CSharp` and `System.Text.Json` during restore — resolved by removing those packages.
