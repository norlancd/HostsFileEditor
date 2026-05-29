# SPEC-03 — Rollback Timer

**Status:** Proposed  
**Priority:** P2  
**Depends on:** SPEC-01 (Profiles), SPEC-02 (Diff Before Switch), SPEC-09 (Audit Log)

---

## Objective

Apply a profile for a bounded duration, then prompt the user to revert — with options to extend, confirm, or cancel the auto-revert. Timer state survives app restarts. The canonical failure mode this prevents: a developer switches to a "staging" profile for a quick test, gets pulled into a meeting, and returns 4 hours later to find all production traffic still misrouted on their machine.

---

## Current State

No timer functionality exists. Profile activation (`HostsFile.Instance.Import`) is permanent until manually changed.

---

## Data Model

### `RollbackTimer` (persisted)

Stored at `%APPDATA%\HostsFileEditor\rollback-timer.json`. Written with `System.Text.Json`. File is created on timer start, deleted on completion/cancellation.

```json
{
  "activatedProfileName": "staging",
  "snapshotArchiveName": "__rollback_20260529_143201",
  "activatedAt": "2026-05-29T14:32:01Z",
  "expiresAt": "2026-05-29T15:02:01Z",
  "status": "Active",
  "snoozeUntil": null,
  "snapshotHash": "sha256:abc123...",
  "appVersion": "1.3.0"
}
```

`status` enum: `Active | Snoozed | Cancelled | Completed`

### Snapshot Archive

Before activating a timed profile, the **current** hosts file is snapshotted to `...\etc\archive\__rollback_{yyyyMMdd_HHmmss}`. This is a standard archive file (same format as all other archives). The `snapshotHash` in the JSON is the SHA-256 of this file's contents at creation time, used to verify integrity before restoring.

Snapshot archives are excluded from the normal archive list UI (filtered by `__rollback_` prefix). They appear in a separate "Rollback Snapshots" section in the archive panel, read-only.

Only one rollback snapshot exists at a time. Starting a new timed switch when a timer is already active cancels the previous timer (after confirmation) and replaces the snapshot.

---

## Timer Lifecycle

### Activation (via `RollbackTimerService.Start`)

1. Assert: no active timer. If one exists, prompt: "A rollback timer is active for profile 'X'. Cancel it and start a new one?"
2. Snapshot current hosts file → `__rollback_{timestamp}` archive.
3. Compute and store `snapshotHash`.
4. Write `rollback-timer.json` with `status = Active`.
5. Activate the new profile via `ProfileSwitcher.Activate` (SPEC-01 flow, including diff dialog).
6. Start in-process `System.Threading.Timer` with interval = `ExpiresAt - UtcNow`.
7. Log to audit log (SPEC-09): `action = TimedProfileSwitch`.

### On Expiry (timer callback)

The callback marshals to the UI thread via `SynchronizationContext.Post`:

1. Verify the snapshot file still exists and its SHA-256 matches `snapshotHash`. If not: log error, show tray balloon "Rollback snapshot is missing or corrupted. Auto-revert cancelled.", set `status = Cancelled`, delete timer JSON, stop.
2. Verify the live hosts file has not been manually modified since activation (compare `File.GetLastWriteTimeUtc` against `ActivatedAt`). If modified externally: include a warning in the expiry dialog.
3. Show `RollbackExpiryForm` (see below).

### `RollbackExpiryForm`

Modal, always-on-top, cannot be minimized. Appears even if main window is hidden.

```
┌──────────────────────────────────────────────────────┐
│  ⏱  Timed profile expiring                          │
│                                                      │
│  Profile "staging" was activated 30 minutes ago.    │
│  Revert to previous configuration?                   │
│                                                      │
│  [■■■■■■■■■■░░░░░░░░░░]  Auto-reverts in 47s        │
│                                                      │
│  [Revert Now]  [Keep 30 min more]  [Keep Permanently]│
└──────────────────────────────────────────────────────┘
```

- Progress bar counts down 60 seconds. If no interaction: **auto-revert executes**.
- **Revert Now**: immediately calls `RollbackTimerService.ExecuteRevert()`.
- **Keep 30 min more**: sets `snoozeUntil = UtcNow + 30min`, `status = Snoozed`, restarts in-process timer, closes dialog.
- **Keep Permanently**: sets `status = Cancelled`, deletes snapshot archive and timer JSON, closes dialog. Logs `TimerCancelled` to audit log.
- If the live file was modified externally, add warning text below the description: "⚠ The hosts file was modified externally after this profile was activated. Reverting may overwrite those changes."

### `RollbackTimerService.ExecuteRevert`

1. Read snapshot file from disk, verify SHA-256.
2. `HostsFile.Instance.Import(snapshotPath)` → `HostsFile.Instance.Save()`.
3. Show diff (SPEC-02) of what was reverted — informational only, no confirmation needed (revert was already confirmed).
4. Delete snapshot archive file.
5. Set `status = Completed`, delete `rollback-timer.json`.
6. Log to audit log: `action = RollbackExecuted`, `autoReverted = true/false`.
7. Tray balloon: "Hosts file reverted to configuration before 'staging' was activated."

---

## App Restart Recovery

In `Program.Main`, before creating `MainForm`, call `RollbackTimerService.RecoverFromRestart()`:

```
if rollback-timer.json exists:
    if status == Active or Snoozed:
        if ExpiresAt (or SnoozeUntil) < UtcNow:
            ExecuteRevert() immediately, show tray balloon
        else:
            restart in-process timer with remaining duration
    if status == Cancelled or Completed:
        delete rollback-timer.json (cleanup from unclean exit)
```

---

## Duration Options UI

When activating a profile from the tray menu or main form, a "Activate with timer…" option opens a small `TimerDurationForm`:

- Preset buttons: 15 min / 30 min / 1 hour / 2 hours
- Custom: numeric input + unit dropdown (minutes / hours)
- "No timer" option (standard permanent activation)

---

## Security Notes

- The snapshot file is written to `...\etc\archive\` which requires elevation to write. Non-elevated processes cannot tamper with it.
- SHA-256 hash verification before every revert prevents a compromised or replaced snapshot from being silently applied.
- `rollback-timer.json` lives in `%APPDATA%` (user-writable). An attacker with user-level access could corrupt it. This would cause revert failure (logged) but cannot cause an incorrect hosts file to be written — the snapshot hash verification catches this.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| App closed during active timer, never reopened | Timer fires on next app launch via recovery. |
| Machine hibernated past expiry | Same as above — recovery checks wall-clock time, not elapsed time. |
| User manually changes profile while timer is active | Detect on expiry via external modification check. Warn in dialog before reverting. |
| Snapshot archive deleted externally before expiry | Detected on expiry. Timer cancelled, user notified. |
| Multiple rapid switches in < 60s | Each new timed switch requires cancelling the previous one (confirmation prompt). Prevents snapshot chain confusion. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/RollbackTimer.cs` | New — persisted data model |
| `src/RollbackTimerService.cs` | New — lifecycle management, recovery, revert logic |
| `src/RollbackExpiryForm.cs` | New — expiry dialog with countdown |
| `src/TimerDurationForm.cs` | New — duration picker |
| `src/Program.cs` | Call `RollbackTimerService.RecoverFromRestart()` on startup |
| `src/MainForm.cs` | Add "Activate with timer…" to archive context menu and tray profiles submenu |
