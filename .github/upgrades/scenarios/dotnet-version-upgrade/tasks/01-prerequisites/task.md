# 01-prerequisites: Verify SDK and update global.json

Verify that the .NET 10 SDK is installed and compatible. The repo has a `global.json` file (observed in the workspace) which must be updated to reference the .NET 10 SDK version, otherwise the build will reject the new TFM. Also validate that the Windows App SDK and other toolchain dependencies are compatible with net10.0-windows.

**Done when**: .NET 10 SDK is confirmed installed; `global.json` is updated to a compatible .NET 10 SDK version; solution restores without SDK-version errors.

---

## Research Findings

- **SDK**: `validate_dotnet_sdk_installation` confirmed .NET 10 SDK is installed.
- **global.json**: Was pinned to `9.0.305` with `rollForward: latestFeature`. The `validate_dotnet_sdk_in_globaljson` tool updated `rollForward` to `latestMajor`, allowing the .NET 10 SDK to be used without changing the base version pin.
- **Restore**: `dotnet restore` completed successfully — 4 projects restored, no SDK-version errors.
- **MSTest.Sdk**: `msbuild-sdks` entry stays as-is (`3.11.0`) — no change needed for the prerequisites task.
