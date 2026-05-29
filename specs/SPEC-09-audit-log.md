# SPEC-09 — Audit Log

**Status:** Proposed  
**Priority:** P0  
**Depends on:** *(none — must be implemented before any new write path is added)*

---

## Objective

Maintain a tamper-evident, append-only log of all changes to the hosts file for compliance, incident response, and debugging. The hosts file is a critical security control surface — any change can redirect DNS for any domain on the machine. Knowing who changed what and when is non-negotiable in professional and enterprise environments.

---

## Current State

No logging exists. When the hosts file changes — via the GUI, via an archive restore, or via an external editor — there is no record of what changed, when, or why.

---

## Log Location

`%APPDATA%\HostsFileEditor\audit.log`

Format: **JSON Lines** (NDJSON) — one JSON object per line. This format is:
- Streamable (tail -f equivalent)
- Parseable line-by-line without loading the full file
- Compatible with all major SIEM and log aggregation tools (Splunk, ELK, Datadog)
- Human-readable with `jq`

---

## Log Entry Schema

All fields are always present (no optional/null fields except where noted).

```json
{
  "seq": 42,
  "timestamp": "2026-05-29T14:32:01.123Z",
  "previousHash": "sha256:abc123def456...",
  "action": "ProfileSwitch",
  "actor": "WORKSTATION-01\\norlan",
  "source": "TrayHotkey",
  "appVersion": "1.3.0",
  "machineHostname": "WORKSTATION-01",
  "detail": {
    "profileFrom": "default",
    "profileTo": "staging",
    "bypassedDiff": false,
    "rollbackTimerMinutes": 30,
    "entriesAdded": [
      { "ip": "10.0.0.50", "hostnames": "api.staging.local", "enabled": true }
    ],
    "entriesRemoved": [
      { "ip": "192.168.1.10", "hostnames": "api.local", "enabled": true }
    ],
    "entriesModified": [
      {
        "before": { "ip": "192.168.1.10", "hostnames": "db.local", "enabled": true },
        "after":  { "ip": "10.0.0.51",   "hostnames": "db.local", "enabled": true }
      }
    ]
  }
}
```

### Action Types

| Action | Trigger | Key Detail Fields |
|---|---|---|
| `ProfileSwitch` | Archive loaded / profile activated | `profileFrom`, `profileTo`, `bypassedDiff`, entries diff |
| `EntryAdded` | Single entry added in grid or via CLI | `entry` |
| `EntryRemoved` | Single entry removed in grid or via CLI | `entry` |
| `EntryModified` | Field edited in grid or via CLI | `before`, `after` |
| `HostsFileEnabled` | Hosts file renamed back from `.disabled` | — |
| `HostsFileDisabled` | Hosts file renamed to `.disabled` | — |
| `TimedProfileSwitch` | Profile switch with rollback timer | + `rollbackTimerMinutes` |
| `RollbackExecuted` | Timer expired, revert applied | `autoReverted: bool`, `profileReverted` |
| `TimerCancelled` | User cancelled rollback timer | `profileKept` |
| `DefaultRestored` | "Restore Default" clicked | — |
| `FileImported` | External file imported | `sourcePath` |
| `UrlImported` | URL import (SPEC-10) | `sourceUrl`, `entriesAdded`, `entriesRemoved` |
| `AliasUpdated` | IP alias value changed (SPEC-07) | `aliasName`, `oldValue`, `newValue`, `affectedEntries: int` |

### `source` Values

`MainForm | TrayMenu | TrayHotkey | CLI | RollbackTimer | UrlScheduler`

### `actor` Field

`Environment.UserDomainName + "\\" + Environment.UserName`. In non-domain environments: `MACHINENAME\username`. This is the Windows identity of the process — since the app runs elevated, this is the user who launched it (UAC preserves the original user identity in `UserName`).

---

## Tamper Evidence: Hash Chain

Each log entry includes `previousHash`: the SHA-256 hash of the raw JSON text of the **previous log line** (including its newline character). The first entry in the log has `previousHash: "genesis"`.

This creates a chain: modifying or deleting any log line makes all subsequent `previousHash` values invalid. The chain can be validated by any external tool.

`AuditLogger` validates chain integrity on startup (last 100 entries only, for performance). If a break is detected: tray balloon warning "Audit log integrity check failed — possible tampering detected." The app continues normally but logs a `ChainBreakDetected` entry.

Chain validation is also exposed via CLI: `hostseditor audit verify`.

---

## Batching for Multi-Entry Operations

`ProfileSwitch` logs the full diff in `entriesAdded` / `entriesRemoved` / `entriesModified` arrays rather than one log entry per entry. This keeps profile switches as a single auditable event. The individual `EntryAdded`/`EntryModified`/`EntryRemoved` actions are for single-entry edits in the grid.

`ProfileSwitch` replaces individual entry events for that operation — there is no double-logging.

---

## `AuditLogger` (new singleton)

```csharp
public class AuditLogger
{
    public static AuditLogger Instance { get; }

    public void Log(AuditEntry entry);
    public IEnumerable<AuditEntry> ReadEntries(DateTime? from = null, DateTime? to = null);
    public bool VerifyChain(int lastN = 100);
}
```

`Log()` is synchronous (file append with `FileShare.Read`). Log writes are fast enough (< 1ms) that async is not needed and synchronous simplifies thread safety.

Thread safety: `Log()` uses a `lock` on a static object. Multiple threads (CLI + GUI simultaneously) serialize via OS file locking (`FileShare.None` for the write duration, opened fresh per write).

---

## Log Rotation

- Rotate at 10MB.
- Rotated files: `audit.log.1`, `audit.log.2`, ..., `audit.log.5`. Maximum 5 rotated files (50MB total).
- On rotation: the first entry of the new `audit.log` includes `previousHash` of the last entry of `audit.log.1` — chain is not broken across rotation boundaries.
- Old `audit.log.6` is deleted on rotation.
- Rotation check happens before every `Log()` call.

---

## Audit Log Viewer (UI)

Accessible from `File > View Audit Log…`. Opens `AuditLogForm` — read-only, resizable.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Audit Log                          [Filter: ________] [Export CSV]  │
├──────┬──────────────────┬────────────────┬──────────┬────────────────┤
│  #   │  Timestamp       │  Action        │  Source  │  Summary       │
├──────┼──────────────────┼────────────────┼──────────┼────────────────┤
│  42  │  14:32:01 today  │  ProfileSwitch │  Hotkey  │  default→stag  │
│  41  │  11:15:44 today  │  EntryModified │  MainForm│  api.local IP  │
│  40  │  yesterday       │  HostsDisabled │  TrayMenu│                │
└──────┴──────────────────┴────────────────┴──────────┴────────────────┘
│  ▼ Entry #42 detail                                                   │
│  Profile: default → staging  |  Source: TrayHotkey  |  Timer: 30min  │
│  + Added:    10.0.0.50  api.staging.local                            │
│  − Removed:  192.168.1.10  api.local                                  │
│  ~ Modified: db.local  192.168.1.10 → 10.0.0.51                      │
└──────────────────────────────────────────────────────────────────────┘
```

- Click a row to expand the detail panel.
- Filter by: date range, action type, source.
- "Export CSV": writes all visible (filtered) entries to a `.csv` file for SIEM ingestion.
- Chain integrity indicator at the bottom: "✓ Chain integrity verified" or "⚠ Integrity check failed."

---

## Integration Points

All existing write operations in `MainForm` must be wrapped with `AuditLogger.Instance.Log(...)` calls:

| Method | Audit Action |
|---|---|
| `OnSaveClick` → `HostsFile.Instance.Save()` | `EntryModified` (for each dirty entry, or batch if many) |
| `OnArchiveLoadClick` | `ProfileSwitch` |
| `OnRestoreClick` | `DefaultRestored` |
| `OnImportClick` | `FileImported` |
| `OnDisableHostsClick` | `HostsFileEnabled` / `HostsFileDisabled` |

For individual cell edits: `HostsEntry.PropertyChanged` → `AuditLogger` deferred write (batched per `UndoManager.BatchActions` scope).

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| `%APPDATA%` path not writable | Log silently to a temp path, notify on first failure. App does not crash. |
| Disk full during log write | Catch `IOException`, show tray balloon once. Skip logging for that session. |
| Log file deleted externally | Next write recreates it. Chain starts fresh (`previousHash: "genesis"`). No error. |
| Log file corrupted (non-JSON line) | `ReadEntries` skips unparseable lines, continues. `VerifyChain` reports break at that position. |
| Multiple app instances (GUI + CLI) writing simultaneously | File-level locking (`FileShare.None` during append). Retry 3 times with 50ms delay before giving up on that log entry. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/AuditLogger.cs` | New — singleton, append, hash chain, rotation |
| `src/AuditEntry.cs` | New — log entry model + action enum |
| `src/AuditLogForm.cs` | New — read-only viewer dialog |
| `src/MainForm.cs` | Add `AuditLogger` calls to all write event handlers |
| `src/Program.cs` | Initialize `AuditLogger` on startup |
