# SPEC-11 — Automatic Backup Before Save

**Status:** Proposed  
**Priority:** P1  
**Depends on:** *(none — purely additive, no dependencies)*

---

## Objective

Silently create a versioned backup of the hosts file before every write, providing a safety net without user intervention. The current `.bak` file (created on app load) is overwritten each session and cannot be used for rollback after a save.

---

## Current State

`HostsFile` constructor copies the hosts file to `hosts.bak` once on load:

```csharp
File.Copy(filePath, DefaultBackupHostFilePath, true);
```

This single file is overwritten on each app start. There is no per-save versioning. After 3 saves in a session, `hosts.bak` reflects only the state at startup — not any intermediate state.

---

## Backup Location

`C:\Windows\System32\drivers\etc\archive\__autobak\`

This directory is within the existing archive subtree, requires elevation to write (consistent with all other archive operations), and is separated from user-created archives by the `__autobak` prefix.

---

## Backup Naming

`hosts_{yyyyMMdd_HHmmss}` — no extension (consistent with existing archive files).

Example: `hosts_20260529_143201`

Timestamp is local time (not UTC) for human readability when browsing in Explorer.

---

## When Backups Are Created

Before every call to `HostsFile.SaveAs(saveFilePath)` where `saveFilePath == DefaultHostFilePath` (i.e., saving to the live hosts file, not `SaveAs` to an arbitrary path or archive).

```csharp
public void Save()
{
    AutoBackupService.Instance.CreateBackup();   // new
    SaveAs(filePath);
    NativeMethods.FlushDns();
}
```

`AutoBackupService.Instance.CreateBackup()` is a no-op when:
- `AutoBackupEnabled` setting is false.
- The hosts file does not exist (disabled state — nothing to back up).
- A backup with the same content hash already exists (deduplication — prevents redundant backups when saving without changes).

---

## `AutoBackupService` (new singleton)

```csharp
public void CreateBackup()
{
    if (!Settings.Default.AutoBackupEnabled) return;
    if (!File.Exists(HostsFile.DefaultHostFilePath)) return;

    var content = File.ReadAllBytes(HostsFile.DefaultHostFilePath);
    var hash = ComputeSha256(content);

    if (hash == lastBackupHash) return;   // no change since last backup

    EnsureDirectory();
    var backupPath = Path.Combine(AutoBackupDirectory, $"hosts_{DateTime.Now:yyyyMMdd_HHmmss}");

    using (FileEx.DisableAttributes(backupPath, FileAttributes.ReadOnly))
        File.WriteAllBytes(backupPath, content);

    lastBackupHash = hash;
    EnforceRetentionLimit();
}
```

`lastBackupHash` is in-memory only (not persisted). On app restart, the first save always creates a backup (hash is unknown).

### Retention

`EnforceRetentionLimit()`: keep the most recent `AutoBackupMaxCount` files in the `__autobak\` directory. Delete the oldest when count exceeds the limit.

Sorting is by filename (timestamp-based names sort lexicographically = chronologically). No need to read file metadata.

Default `AutoBackupMaxCount = 20`.

---

## UI: Auto-Backups in Archive Panel

Auto-backups are visible in the `HostsArchiveDataGridView` as a separate section or with a distinct visual style:

- **Separate section** approach: a non-selectable header row "Automatic Backups (20)" separating them from user-created archives. The section is collapsed by default and can be expanded.
- Auto-backup rows: read-only icon, timestamp as display name (`Today 14:32:01`, `Yesterday 11:15`, `2026-05-28`), no rename option.
- Right-click on auto-backup: **Restore** (triggers SPEC-02 diff preview first), **Delete**, **Save as Profile…** (promotes to a named user archive).
- Auto-backups cannot be renamed. They cannot be activated via the global hotkey system (no metadata file).

`HostsArchiveList.Refresh()` is updated to enumerate the `__autobak\` subdirectory separately. Auto-backups are represented as `HostsArchive` instances with a `IsAutoBackup = true` flag.

---

## Deduplication

The hash check (`if (hash == lastBackupHash) return`) prevents creating a backup when:
- User presses Save multiple times without making changes.
- Auto-save triggers after an undo that returned the file to its previous state.

This is content-based deduplication (not time-based), so even if the app is restarted and the file has not changed, the first save after restart will create a backup (safe, because `lastBackupHash` is reset to null on startup).

---

## Settings Added

```
AutoBackupEnabled     : bool   (default: true)
AutoBackupMaxCount    : int    (default: 20)
```

Accessible via `Tools > Options > Backups` tab (or inline in existing settings if no Options dialog exists — add to the "Auto-ping" area in the menu).

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Disk full during backup | Catch `IOException`, log to audit log (SPEC-09) if available, show tray balloon once per session. **Do not fail the Save** — backup failure must never block the user from saving. |
| Hosts file does not exist (disabled state) | `CreateBackup` returns immediately — nothing to back up. |
| `__autobak\` directory does not exist | Created on first backup (`Directory.CreateDirectory`). |
| Two saves within the same second | Timestamp collision: append `_1`, `_2` suffix. `hosts_20260529_143201_1`. |
| User manually deletes auto-backups in Explorer | `EnforceRetentionLimit` only deletes files it finds; an already-deleted file causes no error. |
| `AutoBackupMaxCount = 0` | Disables auto-backup (equivalent to `AutoBackupEnabled = false`). |

---

## What Auto-Backup Does NOT Do

- Does not replace the rollback timer snapshot (SPEC-03) — that snapshot is purpose-built for a specific revert with integrity verification.
- Does not replace manual archives — auto-backups are safety net copies, not named configurations.
- Does not sync or replicate anywhere — local only.
- Does not back up archive files — only the live hosts file.

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/AutoBackupService.cs` | New — singleton, backup creation, retention |
| `src/HostsFile.cs` | Call `AutoBackupService.Instance.CreateBackup()` in `Save()` |
| `src/HostsArchive.cs` | Add `IsAutoBackup : bool` property |
| `src/HostsArchiveList.cs` | Enumerate `__autobak\` separately, mark entries |
| `src/Controls/HostsArchiveDataGridView.cs` | Render auto-backup section header, read-only row style |
| `src/Properties/Settings.settings` | Add `AutoBackupEnabled`, `AutoBackupMaxCount` |
