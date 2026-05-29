# .NET 10 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that a .NET 10.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10.0 upgrade.
3. Upgrade src/HostsFileEditor.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

No projects are excluded.

### Aggregate NuGet packages modifications across all projects

| Package Name                                        | Current Version      | New Version | Description                                                              |
|:----------------------------------------------------|:--------------------:|:-----------:|:-------------------------------------------------------------------------|
| Equin.ApplicationFramework.BindingListView          | 1.4.5222.35545       |             | No supported version found for .NET 10.0 - incompatible package         |
| System.Resources.ResourceManager                   | 4.3.0                |             | Package functionality included with new framework reference - can remove |

### Project upgrade details

#### src/HostsFileEditor.csproj modifications

Project properties changes:
  - Target framework should be changed from `net9.0-windows` to `net10.0-windows`

NuGet packages changes:
  - `Equin.ApplicationFramework.BindingListView` (1.4.5222.35545) has no supported version for .NET 10.0 — manual review required
  - `System.Resources.ResourceManager` (4.3.0) should be removed — functionality is now included in the framework reference
