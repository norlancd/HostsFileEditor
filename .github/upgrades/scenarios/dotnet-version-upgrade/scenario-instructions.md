# .NET Version Upgrade

## Strategy
**Selected**: All-at-Once — upgrade all 4 projects simultaneously.
**Rationale**: 4 projects, all modern .NET (net9.0), all SDK-style, 2-tier depth.

### Execution Constraints
- Single atomic upgrade — all projects updated together in one pass
- Operation sequence: update TFMs → set up CPM → restore → build → fix all errors (single bounded pass)
- Validate full solution build after upgrade before running tests
- Tests run after the atomic upgrade completes successfully

## Upgrade Options
- **Strategy**: All-at-Once
- **Package Management**: Central Package Management (CPM) — create `Directory.Packages.props`
- **Unsupported Packages**: Resolve Inline (2 packages: `Equin.ApplicationFramework.BindingListView`, `H.NotifyIcon.WinUI`)
- **Unsupported API Handling**: Fix Inline

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0 (LTS)

## Source Control
- **Source Branch**: winui3_migration
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: After Each Task
