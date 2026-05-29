# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade all projects from net9.0 / net9.0-windows to net10.0 / net10.0-windows
**Scope**: 4 projects — 1 class library, 1 WinForms app, 1 WinUI app, 1 test project

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: 4 projects, all on modern .NET (net9.0), all SDK-style, 2-tier dependency depth. Straightforward TFM bump with package updates and API fixes.

---

## Tasks

### 01-prerequisites: Verify SDK and update global.json

Verify that the .NET 10 SDK is installed and compatible. The repo has a `global.json` file (observed in the workspace) which must be updated to reference the .NET 10 SDK version, otherwise the build will reject the new TFM. Also validate that the Windows App SDK and other toolchain dependencies are compatible with net10.0-windows.

**Done when**: .NET 10 SDK is confirmed installed; `global.json` is updated to a compatible .NET 10 SDK version; solution restores without SDK-version errors.

---

### 02-upgrade-all-projects: Upgrade all projects to net10.0

Update all four projects to target net10.0 simultaneously. This is the core upgrade task:

- **HostsFileEditor.Core** (`net9.0-windows` → `net10.0-windows`): 1 source-incompatible API, 1 NuGet package upgrade (`Microsoft.Extensions.DependencyInjection` 9→10). Small library with no breaking-change risk beyond the flagged items.
- **HostsFileEditor.Core.Tests** (`net9.0-windows` → `net10.0-windows`): TFM change only. Update `System.Resources.Extensions` and `System.Text.Json` to their net10.0 versions.
- **HostsFileEditor.WinForm** (`net9.0-windows` → `net10.0-windows`): Most complex project. Assessment flagged 2,589 binary-incompatible WinForms APIs (type-forwarding/platform changes), 181 source-incompatible APIs, Legacy Configuration usage (17 issues), Windows Forms Legacy Controls (350 issues), GDI+/System.Drawing usage (164 issues). Package `Equin.ApplicationFramework.BindingListView` is incompatible and has no known replacement — requires inline research. `System.Resources.ResourceManager` is now framework-included and should be removed. Start by investigating the WinForms binary-incompatible count: many of these are type-forwarder issues that resolve automatically with a TFM bump.
- **HostsFileEditor.WinUI** (`net9.0-windows10.0.19041.0` → `net10.0-windows10.0.19041.0`): `H.NotifyIcon.WinUI` is incompatible — research a compatible version or alternative. 1 source-incompatible API. Upgrade `Microsoft.Extensions.DependencyInjection` to v10.

Also set up Central Package Management (CPM) as part of this task: create `Directory.Packages.props`, move all version attributes out of project files, and resolve the `System.Resources.Extensions` version divergence (currently at both 9.0.8 and 9.0.9 across projects).

Operation sequence: update all TFMs → set up CPM and move package versions → restore → build solution → fix all compilation errors in a single bounded pass.

**Done when**: All 4 projects target net10.0 (or net10.0-windows); solution builds with 0 errors and 0 warnings; `Directory.Packages.props` exists with all package versions centralized; incompatible packages resolved or removed; all tests pass.

---

### 03-final-validation: Full solution validation and cleanup

Run the full test suite and confirm no regressions. Document any deferred recommendations (e.g., post-migration modernization opportunities surfaced during the upgrade). Commit all changes on the working branch.

**Done when**: All tests pass; working branch committed; no outstanding build warnings; deferred recommendations documented.

