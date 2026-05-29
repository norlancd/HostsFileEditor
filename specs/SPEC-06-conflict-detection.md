# SPEC-06 — Conflict Detection

**Status:** Proposed  
**Priority:** P1  
**Depends on:** *(none — read-only analysis, no write path)*

---

## Objective

Proactively identify and surface logical inconsistencies in the active hosts file that the OS silently ignores. The hosts file has no built-in validation — duplicate entries, conflicting hostname mappings, and disabled-but-shadowing entries all fail silently, often causing hours of debugging.

---

## Current State

`HostsEntry` validates individual entries (IP format, hostname format) via `IDataErrorInfo`. There is no cross-entry analysis. `HostsEntryList.Error` only checks for individually invalid entries.

---

## Conflict Types

| Code | Name | Severity | Description |
|---|---|---|---|
| `C01` | Duplicate Entry | Warning | Identical `IpAddress + HostNames` pair (case-insensitive) appears more than once, whether enabled or disabled. The OS uses the first match — the duplicate is dead weight and confusing. |
| `C02` | Hostname Conflict | Error | The same hostname (or one hostname from a multi-hostname entry) is mapped to **different IPs** in two or more **enabled** entries. The OS uses the first match silently — the second entry is ignored but looks active. |
| `C03` | Public Hostname Redirect | Warning | An enabled entry redirects a hostname from a curated list of known public services (see below) to a non-public IP. Legitimate in dev, dangerous if unintentional or left active. |
| `C04` | Shadowed Entry | Info | An enabled entry for hostname X exists above a disabled entry for the same hostname. The disabled entry is permanently shadowed and will never take effect — likely forgotten during a profile switch. |
| `C05` | Invalid Enabled Entry | Error | `HostsEntry.Enabled == true` but `HostsEntry.Valid == false`. Should be impossible via the UI but can occur after external edits. OS ignores it silently. |
| `C06` | Loopback Redirect of External Hostname | Info | `127.0.0.1` or `::1` mapped to a hostname that is not a local/internal name (heuristic: no `.local`, `.internal`, `.dev`, `.test`, `.localhost` suffix). May be an intentional block or a forgotten dev override. |

### Public Hostname List (C03)

Hardcoded list of hostnames and second-level domains whose redirection warrants a warning. Stored as a resource file. Examples:

- `microsoft.com`, `windows.com`, `windowsupdate.com`
- `google.com`, `googleapis.com`, `gstatic.com`
- `github.com`, `githubusercontent.com`
- `apple.com`, `icloud.com`
- `amazon.com`, `amazonaws.com`
- `login.microsoftonline.com`, `live.com`

Matching: the entry's hostname ends with any listed domain (suffix match, case-insensitive). Wildcard entries like `*.microsoft.com` match all subdomains.

---

## `ConflictAnalyzer` (new static class)

`ConflictAnalyzer.Analyze(entries: HostsEntryList) → IReadOnlyList<HostsConflict>`

Runs in O(n) using dictionaries. Returns the full conflict list; callers decide what to display.

```csharp
public record HostsConflict(
    ConflictCode Code,
    ConflictSeverity Severity,
    string Description,
    IReadOnlyList<HostsEntry> Entries   // 1 or 2 entries involved
);
```

`ConflictSeverity`: `Error | Warning | Info`

### Algorithm Sketch

```
hostnameToEntries : Dictionary<string, List<HostsEntry>>   // enabled only
ipHostnamePairs   : HashSet<(string ip, string hostname)>  // all entries

foreach entry in entries:
    if entry.Valid:
        foreach hostname in entry.HostNames.Split(' '):
            hostnameToEntries[hostname].Add(entry)
            if ipHostnamePairs.Contains((ip, hostname)):  → C01
            else: ipHostnamePairs.Add((ip, hostname))

foreach (hostname, list) in hostnameToEntries:
    if list.Count > 1 and list has distinct IPs:  → C02
    if any hostname matches public domain list:   → C03

foreach entry where !entry.Valid && entry.Enabled:  → C05
foreach entry where entry is loopback to external hostname:  → C06
```

---

## UI: Conflict Panel

A collapsible panel docked at the bottom of `MainForm`, between the grid and the status bar. Collapsed by default when no `Error`-severity conflicts exist; auto-expanded when an `Error` is detected.

```
┌──────────────────────────────────────────────────────────────────┐
│ ▼ 1 Error  2 Warnings  (click to collapse)                       │
├──────────────────────────────────────────────────────────────────┤
│ ● [Error]   C02  Hostname conflict: "api.local" → 192.168.1.10   │
│                   and 10.0.0.5  — OS uses first match only        │
│                   [Jump to entries ↗]                             │
│ ▲ [Warning] C03  Public hostname redirect: "github.com" →        │
│                   127.0.0.1  — Verify this is intentional         │
│                   [Jump to entry ↗]                               │
│ ℹ [Info]    C06  Loopback redirect: "somecdn.com" → 127.0.0.1   │
└──────────────────────────────────────────────────────────────────┘
```

- **[Jump to entries ↗]**: selects and scrolls to the conflicting rows in the grid. Clears existing selection, selects conflict entries, scrolls first into view.
- Icon: ● red for Error, ▲ yellow for Warning, ℹ blue for Info.
- Panel height: 120px when expanded. Resizable (splitter).

### Row-Level Highlighting in Grid

- `HostsEntryDataGridView` custom `RowPrePaint`: check if `entry` is involved in any conflict. Apply background tint:
  - `Error`: light red `#FFE0E0`
  - `Warning`: light yellow `#FFFBE0`
  - `Info`: light blue `#E8F0FF`
- When the same entry is in multiple conflicts, the highest severity color wins.
- Conflict highlight is a tint over the normal row color (alternating row colors still visible).

---

## Trigger & Debounce

`ConflictAnalyzer.Analyze` is called:
- On `HostsFile.Instance.Entries.ListChanged` — debounced 500ms via `System.Windows.Forms.Timer`.
- On profile switch (after `Import` + `Save`).
- On app load.

The 500ms debounce prevents analysis on every individual keystroke during live editing.

Analysis runs on the UI thread (fast enough for typical hosts files; O(n) with n < 1000 entries is negligible). If a profile ever has > 5000 entries, move to `Task.Run` with result marshalling.

---

## `HostsEntryList` Integration

`HostsEntryList` gains a `Conflicts : IReadOnlyList<HostsConflict>` property, refreshed by the analyzer. This allows `ConflictPanel` and the grid row painter to share the same result without re-running analysis.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| User fixes a conflict while panel is open | Next `ListChanged` triggers debounced re-analysis. Conflict disappears from panel within 500ms. Grid highlight removed. |
| 10,000-entry file (e.g. imported ad blocklist) | Analysis is O(n). 10k entries processes in < 5ms on modern hardware. Still synchronous. |
| Entry with multiple hostnames in `C02` | Each hostname is checked independently. If `api.local db.local` is mapped to one IP and `api.local` alone to another IP, that is a conflict on `api.local` only — `db.local` is clean. |
| Profile with only comment lines | No valid entries → no conflicts. Panel hidden. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/ConflictAnalyzer.cs` | New — analysis logic |
| `src/HostsConflict.cs` | New — conflict record + enums |
| `src/HostsEntryList.cs` | Add `Conflicts` property |
| `src/Controls/ConflictPanel.cs` | New — docked panel UI |
| `src/Controls/HostsEntryDataGridView.cs` | Add conflict row highlighting in `RowPrePaint` |
| `src/MainForm.cs` | Add `ConflictPanel` to form layout, wire `ListChanged` debounce |
| `src/Resources/PublicHostnames.txt` | New — curated public hostname list |
