# SPEC-10 — Import from URL (Blocklist Feeds)

**Status:** Proposed  
**Priority:** P4  
**Depends on:** SPEC-02 (Diff Before Switch), SPEC-06 (Conflict Detection), SPEC-09 (Audit Log)

---

## Objective

Download and merge external hosts files (ad/tracker/malware blocklists, shared team configurations) into the active profile, with optional scheduling and intelligent conflict handling. The most common use case is merging a blocklist like [StevenBlack/hosts](https://github.com/StevenBlack/hosts) while keeping personal entries intact.

---

## Current State

`MainForm.OnImportClick` opens a file dialog and calls `HostsFile.Instance.Import(filePath)`, which **replaces** all entries. There is no merge, no URL support, and no scheduling.

---

## Design Decisions

- **Merge, not replace.** URL imports are additive by default. User-created entries are never removed by an import. Only previously-imported entries from the same source URL can be updated or removed.
- **HTTPS only.** HTTP URLs are rejected by default. Override requires explicit user confirmation with a security warning.
- **Tag-based tracking.** Imported entries carry a comment sentinel `[src:{url-hash}]` to identify their origin. This allows the app to diff updates against the previous import from the same source.
- **10MB limit.** Files larger than 10MB are rejected before parsing. The largest real-world blocklist (StevenBlack unified) is ~3MB.

---

## Data Model

### `UrlImportSource`

Persisted in `%APPDATA%\HostsFileEditor\url-imports.json`.

```json
{
  "sources": [
    {
      "id": "a1b2c3d4",
      "url": "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts",
      "label": "StevenBlack Unified",
      "lastImportedAt": "2026-05-29T10:00:00Z",
      "lastFileHash": "sha256:abc123...",
      "entriesCount": 140000,
      "scheduleType": "Weekly",
      "nextScheduledAt": "2026-06-05T10:00:00Z",
      "enabled": true
    }
  ]
}
```

`id`: 8-char hex derived from URL hash. Used in the `[src:{id}]` comment sentinel on imported entries.

`scheduleType`: `Manual | Daily | Weekly`

### Entry Sentinel

Imported entries are written to the hosts file with a comment suffix:

```
0.0.0.0   doubleclick.net   # [src:a1b2c3d4]
0.0.0.0   ads.google.com    # [src:a1b2c3d4]
```

On load, `HostsEntry` detects the `[src:{id}]` sentinel and sets `ImportSourceId : string?`. This is used to identify entries for update/removal on re-import.

---

## One-Time Import Flow

`File > Import from URL…` opens `UrlImportForm`:

1. User enters URL and optional label.
2. Validation: must be `https://` (or user confirms HTTP with warning dialog). URL must parse as a valid `Uri`.
3. Download: `HttpClient` with 30-second timeout, `MaxResponseContentBufferSize = 10MB`. If > 10MB: reject.
4. Validate downloaded content: parse as `HostsEntryList`. Must contain at least 1 valid entry. If parse fails: show error, abort.
5. Compute SHA-256 of downloaded content. If matches `lastFileHash` for this URL: show info "No changes since last import." User can still proceed.
6. Compute import diff:
   - **New entries** (in download, not currently in active file by ip+hostname)
   - **Updated entries** (same hostname, different IP vs. currently imported entries — identified by `[src:id]`)
   - **Removed entries** (previously imported from this source, no longer in download)
   - **Skipped entries** (conflict with user entries — see Conflict Handling below)
7. Show summary dialog: "Will add X entries, update Y, remove Z, skip W (conflicts)." With expandable details. Buttons: **Import**, **Cancel**.
8. On confirm: apply changes via `HostsFile.Instance.Entries.BatchUpdate` + `UndoManager.Instance.SuspendUndoRedo` (import is not undoable — too many entries), then `Save()`.
9. Log to audit log (SPEC-09): `UrlImported`.

---

## Conflict Handling

**User entries take priority.** An imported entry that conflicts with a user-created entry (same hostname, different IP — C02 from SPEC-06) is **skipped** and listed in the import summary as "Skipped (conflicts with your entry)."

The user can override this per-conflict in the summary dialog ("Replace my entry with imported value?").

Imported entries never replace or modify entries without the `[src:{id}]` sentinel.

---

## Re-Import (Scheduled Refresh)

`UrlImportScheduler` (new background service) runs on app startup and hourly tick:

```
foreach source where source.Enabled and source.ScheduleType != Manual:
    if UtcNow >= source.NextScheduledAt:
        RunImport(source, silent: true)
```

**Silent import** (no dialog):
- Download and hash check. If hash unchanged: skip.
- Compute diff. If no changes: skip.
- If changes exist: apply automatically (merge, not replace) and show tray balloon: "Updated 'StevenBlack Unified': +42 added, −8 removed."
- If any imported entry now conflicts with a user entry: skip conflicting entries, include in tray balloon: "5 entries skipped (conflict with your entries)."
- Log to audit log.

---

## Security Notes

### Network Security
- HTTPS required by default. HTTPS provides: transport encryption, server identity verification, and tamper detection in transit.
- HTTP override shows: "⚠ HTTP is unencrypted and unverified. Anyone on your network can modify this content before it reaches you. Proceed anyway?" with a checkbox "Don't ask again for this URL" (opt-in, not default).
- Certificate validation uses the default `HttpClient` handler (OS trust store). No certificate pinning — too brittle for community-maintained hosts files.

### Content Security
- Downloaded content is **never executed or evaluated** — only parsed as plain text via `HostsEntryList`. The parser extracts only IP addresses and hostnames; all other content is treated as comments or invalid lines.
- `IpAddress` is validated via `IPAddress.TryParse` per entry before any entry is accepted. A malicious feed cannot inject shell commands or malformed data into the hosts file — the worst it can do is add unexpected IP→hostname mappings, which the diff preview shows.
- File hash is stored and checked on re-import. A feed that changes between scheduled checks is treated as a normal update (diff shown or silently applied). There is no automatic trust escalation.

### URL Allowlist (Enterprise)
- Optional setting `ImportUrlAllowlist : string[]` (default empty = allow all HTTPS).
- If non-empty: only URLs whose `Uri.Host` matches an entry in the allowlist are permitted.
- Allowlist is stored in `Settings` (user-scoped). Admins deploying via GPO can configure this via a machine-scoped config file (future: SPEC-level).

---

## `UrlImportForm` UI

```
┌───────────────────────────────────────────────────┐
│  Import Hosts from URL                            │
├───────────────────────────────────────────────────┤
│  URL:    [https://...                          ]  │
│  Label:  [StevenBlack Unified               ]     │
│                                                   │
│  Schedule: ( ) Manual  (•) Daily  ( ) Weekly      │
│                                                   │
│  [ ] Enable automatic refresh                     │
│                                                   │
│                      [Cancel]  [Download & Preview]│
└───────────────────────────────────────────────────┘
```

After download and analysis, the form expands to show the import summary before the final confirm button.

### Manage Import Sources

`File > Manage URL Imports…` — table of all configured sources with columns: Label | URL | Last Import | Next Schedule | Entries | Status. Actions: Add, Edit, Delete, Import Now.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| URL returns 404 or network error | Show error in dialog (one-time) or tray balloon (scheduled). Do not remove previously imported entries. Retry on next scheduled interval. |
| Download partially succeeds (truncated response) | Hash of partial content will not match stored hash. If this is a first import, the content is parsed anyway — if fewer than expected entries, show warning. |
| Feed changes an entry that user has also customized | Detected as conflict. Skipped silently in scheduled mode; listed for review in manual mode. |
| User manually edits an imported entry (modifying IP) | The `[src:id]` sentinel remains in the comment. On next import, this entry is treated as a conflict (user-modified) and skipped. The sentinel is only removed if the user explicitly removes it from the comment field. |
| App is closed between schedule and scheduled time | On next startup, `UrlImportScheduler.RecoverFromRestart` checks all sources for past-due schedules and runs them. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/UrlImportSource.cs` | New — data model |
| `src/UrlImportStore.cs` | New — JSON persistence |
| `src/UrlImporter.cs` | New — download, parse, diff, apply logic |
| `src/UrlImportScheduler.cs` | New — background scheduler |
| `src/UrlImportForm.cs` | New — dialog: URL input + summary |
| `src/ManageUrlImportsForm.cs` | New — source management table |
| `src/HostsEntry.cs` | Add `ImportSourceId` sentinel parsing/serialization |
| `src/MainForm.cs` | Add `File > Import from URL…` and `File > Manage URL Imports…` menu items |
| `src/Properties/Settings.settings` | Add `ImportUrlAllowlist : StringCollection` |
