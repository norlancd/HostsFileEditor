# 01-prerequisites: Verify SDK and update global.json

Verify that the .NET 10 SDK is installed and compatible. The repo has a `global.json` file (observed in the workspace) which must be updated to reference the .NET 10 SDK version, otherwise the build will reject the new TFM. Also validate that the Windows App SDK and other toolchain dependencies are compatible with net10.0-windows.

**Done when**: .NET 10 SDK is confirmed installed; `global.json` is updated to a compatible .NET 10 SDK version; solution restores without SDK-version errors.
