# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build -c Debug
dotnet build -c Release

# Publish (outputs to .\bin)
dotnet publish -c Release

# Run (requires elevation — writes to C:\Windows\System32\drivers\etc\hosts)
dotnet run --project src/HostsFileEditor.csproj
```

There are no automated tests in this project.

## Architecture

**.NET 9 WinForms app.** Single instance enforced via `ProgramSingleInstance` (Win32 mutex + `WM_SHOWFIRSTINSTANCE` message). Requires administrator elevation (`app.manifest`).

### Core Data Flow

```
hosts file on disk
    → HostsFile (singleton)
        → HostsEntryList (BindingList<HostsEntry>)
            → BindingListView<HostsEntry>  (Equin library — filtering/sorting)
                → MainForm DataGridView (data-bound)
```

Every mutation goes through `HostsEntryList` → auto-registers undo/redo in `UndoManager` → UI reflects via `INotifyPropertyChanged` / `ListChanged` events. After any save, `NativeMethods.FlushDns()` is called — this is mandatory on every write path.

### Key Classes

| Class | Role |
|---|---|
| `HostsFile` | Singleton. Owns the live hosts file path, reads/writes disk, calls `FlushDns`. Also controls enable/disable (rename to `.disabled`). |
| `HostsEntry` | One parsed line. Validates IP via `IPAddress.TryParse`, hostnames via regex. Implements `INotifyPropertyChanged` + `IDataErrorInfo`. Has async `Ping()` via `System.Net.NetworkInformation.Ping`. |
| `HostsEntryList` | `BindingList<HostsEntry>`. All insert/remove operations here register undo/redo actions automatically via overridden `InsertItem`/`RemoveItem`. |
| `HostsArchive` / `HostsArchiveList` | Profile system. Archives are plain hosts-format files stored in `...\etc\archive\`. No metadata beyond filename. |
| `UndoManager` | Singleton. Action-pair stack (undo/redo). Supports `BatchActions()` to group multiple mutations into one undo step, and `SuspendUndoRedo()` for bulk loads. Max 1000 history entries. |
| `HostsFilter` | `IFilterListView` predicate passed to `BindingListView`. Filter + show/hide comments/disabled entries. |

### File Paths

- Live hosts file: `C:\Windows\System32\drivers\etc\hosts`
- Disabled state: `...\etc\hosts.disabled` (renamed, not deleted)
- Backup on open: `...\etc\hosts.bak` (single file, overwritten each load)
- Archives/profiles: `...\etc\archive\{name}` (plain text, no extension)
- User settings: `ApplicationSettingsBase` (user-scoped, standard .NET user config location)

### UI Architecture

`MainForm` is split: left panel = entry grid (`HostsEntryDataGridView`), right panel = archive list (`HostsArchiveDataGridView`), collapsible via splitter. Both grids use `BindingListView` wrapping the respective `BindingList`. The tray icon (`NotifyIcon`) mirrors the enabled/disabled state via icon swap.

`MainForm.OnFormClosing` cancels close and hides to tray instead — `Application.Exit()` via File > Exit is the only real exit path. Settings are saved on exit only.

### UndoManager Nuance

`UndoManager` uses two parallel `LinkedList<LinkedList<Action>>` — one for undo, one for redo. The current position pointer advances forward on each action. During `BatchActions()`, all actions within the lambda share the same linked-list node. `SuspendUndoRedo` must wrap any bulk `AddLines` call (e.g. `Import`, `Refresh`, `RestoreDefault`) to avoid polluting the undo history.

### Adding New Write Operations

Any new operation that modifies `HostsFile.Instance.Entries` must:
1. Use `BatchUpdate()` + `UndoManager.Instance.BatchActions()` if multi-step.
2. Call `HostsFile.Instance.Save()` (not `SaveAs`) to trigger `FlushDns`.
3. If bypassing undo (bulk operation), wrap with `UndoManager.Instance.SuspendUndoRedo()`.

### Planned Features

See [`specs/`](specs/) for full feature specifications covering: global hotkeys, diff-before-switch, rollback timer, continuous ping monitor, DNS resolution column, conflict detection, IP aliases, CLI module, audit log, URL import, auto-backup, and entry groups.
