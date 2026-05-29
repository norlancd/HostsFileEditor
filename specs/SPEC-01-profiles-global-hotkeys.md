# SPEC-01 — Profiles with Global Hotkeys & Tray Quick-Switch

**Status:** Proposed  
**Priority:** P2  
**Depends on:** SPEC-02 (Diff Before Switch), SPEC-09 (Audit Log)

---

## Objective

Allow switching between named host profiles (archives) without opening the main window, via a global keyboard shortcut and/or a tray context menu with visible hotkey labels. The existing `HostsArchive`/`HostsArchiveList` system is the profile store; this spec adds metadata and activation mechanics on top of it.

---

## Current State

`HostsArchive` stores only `FilePath` and derives `FileName`. `HostsArchiveList` enumerates `C:\Windows\System32\drivers\etc\archive\`. Activation requires opening `MainForm`, selecting an archive row, and clicking "Load" — no keyboard shortcut, no tray entry per profile.

---

## New Data Model

Introduce `HostsProfileMetadata` stored as `{archive-name}.json` sidecar alongside each archive file in the archive directory. The raw hosts file remains the source of truth; the `.json` is purely UI metadata and is optional (missing = no hotkey, default color, sort order = 0).

```json
{
  "name": "staging",
  "hotkeyModifiers": 3,
  "hotkeyKey": 49,
  "color": "#4A90D9",
  "sortOrder": 1,
  "description": "Staging environment — points all services to 192.168.10.x"
}
```

`hotkeyModifiers` maps to `System.Windows.Forms.Keys` flags (Control=2, Shift=4, Alt=1). `hotkeyKey` maps to `Keys` enum integer value.

`HostsArchive` gains a lazy-loaded `Metadata : HostsProfileMetadata?` property. `HostsArchiveList` gains a `SaveMetadata(HostsArchive)` method.

---

## Behaviors

### Hotkey Registration

- On app start: enumerate `HostsArchiveList.Instance`, call Win32 `RegisterHotKey(hWnd, id, modifiers, vk)` for each profile with a non-null hotkey. `id` = index in the list (stable per session).
- If `RegisterHotKey` returns false (conflict with existing system hotkey): log warning to tray balloon "Hotkey Ctrl+Shift+1 for profile 'staging' could not be registered — already in use." Skip silently after notifying.
- On profile metadata change (hotkey added/removed/changed): call `UnregisterHotKey` for the old id before calling `RegisterHotKey` for the new one.
- On app exit — `Application.Exit()`, `Application.ThreadException`, and `AppDomain.UnhandledException`: call `UnregisterHotKey` for all registered ids. Register cleanup in `Program.Main` via `Application.ApplicationExit` event, not only in `MainForm.OnFormClosing` (which is suppressed for tray hide).

### Win32 Integration

`MainForm.WndProc` already intercepts `WM_SHOWFIRSTINSTANCE`. Add a `WM_HOTKEY` (0x0312) handler in the same override:

```csharp
if (message.Msg == 0x0312) // WM_HOTKEY
{
    int id = message.WParam.ToInt32();
    var profile = HotkeyRegistry.GetProfileById(id);
    if (profile != null)
        ProfileSwitcher.Activate(profile);
}
```

`HotkeyRegistry` is a new static class managing `id → HostsArchive` mapping and the Win32 registration lifecycle.

### Tray Context Menu

- Add a "Profiles" submenu to the existing `NotifyIcon` context menu, populated from `HostsArchiveList.Instance` sorted by `Metadata.SortOrder`.
- Each item: `{profile.FileName}  (Ctrl+Shift+1)` — hotkey suffix omitted if no hotkey assigned.
- Currently active profile (last one activated via this app or detected by file hash comparison): shown with a checkmark `✓`.
- Rebuild the submenu on every `HostsArchiveList.ListChanged` event.

### Activation Flow

1. User triggers via hotkey or tray menu item.
2. `ProfileSwitcher.Activate(archive)` checks if the archive file exists on disk. If missing: tray balloon error, abort.
3. Load target `HostsEntryList` in memory (do not write yet).
4. If `Settings.DiffBeforeSwitchEnabled` is true: invoke SPEC-02 diff dialog. If user cancels: abort.
5. On confirmation: `HostsFile.Instance.Import(archive.FilePath)` → `HostsFile.Instance.Save()` (which calls `FlushDns` internally).
6. Write entry to audit log (SPEC-09): action=`ProfileSwitch`, source=`TrayHotkey` or `TrayMenu`.
7. Update tray menu checkmark to new active profile.

### Hotkey Assignment UI

- In the archive panel (`HostsArchiveDataGridView` right-click context menu): add "Profile Settings…" item.
- Opens a `ProfileSettingsForm` dialog: fields for Description, Color picker, Sort Order, and Hotkey capture.
- Hotkey field: a `TextBox` that intercepts `KeyDown`, suppresses default input, and displays the chord as text ("Ctrl + Shift + 1"). Backspace clears it.
- Validation on save: reject if the chord is already assigned to another profile (check `HotkeyRegistry`). Reject known dangerous chords: `Win+L`, `Alt+F4`, `Ctrl+Alt+Del`.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| App not elevated | Hotkey fires but `Save()` will fail with `UnauthorizedAccessException`. Show elevation prompt via `ShellExecute` with `runas`. |
| Two profiles, same hotkey | Prevent at `ProfileSettingsForm` save time — inline validation error. |
| Archive file deleted externally | Tray item grayed out. Hotkey fires → tray balloon: "Profile 'staging' file not found." |
| App minimized when hotkey fires | Activate profile silently (no window shown) unless diff dialog is required — then bring dialog to foreground. |
| `HostsArchiveList` empty | Profiles submenu shows disabled item "No profiles saved." |

---

## Settings Added

```
// Settings.settings
DiffBeforeSwitchEnabled : bool  (default: true)
```

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/HostsProfileMetadata.cs` | New class — JSON-serializable metadata model |
| `src/HostsArchive.cs` | Add `Metadata` lazy property |
| `src/HostsArchiveList.cs` | Add `SaveMetadata()`, rebuild on file watch |
| `src/ProfileSwitcher.cs` | New static class — activation logic |
| `src/HotkeyRegistry.cs` | New class — Win32 RegisterHotKey lifecycle |
| `src/MainForm.cs` | Add `WM_HOTKEY` handler in `WndProc`, tray submenu builder |
| `src/ProfileSettingsForm.cs` | New dialog — hotkey capture + metadata editor |
| `src/Win32/NativeMethods.cs` | Add `RegisterHotKey` / `UnregisterHotKey` P/Invoke |
| `src/Properties/Settings.settings` | Add `DiffBeforeSwitchEnabled` |
