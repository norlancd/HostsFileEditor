# SPEC-02 — Diff Before Switch

**Status:** Proposed  
**Priority:** P2  
**Depends on:** *(none — standalone dialog, consumed by SPEC-01 and SPEC-03)*

---

## Objective

Show an exact, human-readable diff of which hosts entries will change before committing any profile switch, preventing accidental DNS resolution changes. A hostname-to-IP modification is functionally equivalent to a local DNS hijack — it must be visible before it happens, not after.

---

## Current State

`HostsFile.Instance.Import(filePath)` calls `Entries.Clear()` + `Entries.AddLines()` inside `BatchUpdate` — immediate, no preview. There is no comparison between the incoming and current state before writing.

---

## Diff Computation

`ProfileDiff.Compute(current: HostsEntryList, incoming: HostsEntryList) → ProfileDiffResult`

Comparison key: `(IpAddress.ToLowerInvariant(), HostNames.ToLowerInvariant())` tuple for enabled entries. Entry identity is by content, not position.

```
ProfileDiffResult
├── Added    : IReadOnlyList<HostsEntry>   // in incoming, not in current (enabled)
├── Removed  : IReadOnlyList<HostsEntry>   // in current, not in incoming (enabled)
├── Modified : IReadOnlyList<DiffPair>     // same hostname(s), different IP
├── Toggled  : IReadOnlyList<DiffPair>     // same IP+hostname, different Enabled state
└── IsEmpty  : bool                        // true if all four lists are empty
```

`DiffPair` holds `Before : HostsEntry` and `After : HostsEntry`.

**Modified entries** are detected by: entries where the same `HostNames` value (case-insensitive) exists in both lists but with a different `IpAddress`. These are the highest-risk changes — a hostname now points somewhere else.

Disabled entries and comment-only lines are excluded from the diff. They are cosmetic and do not affect DNS resolution.

---

## Dialog: `DiffPreviewForm`

Modal dialog, resizable, minimum 500×400.

### Layout

```
┌─────────────────────────────────────────────────────┐
│  Switching to profile: "staging"                    │
│  3 changes to active DNS resolution                 │
├─────────────────────────────────────────────────────┤
│  ⚠ MODIFIED (hostname redirected to a new IP)       │
│  ┌──────────────────────────────────────────────┐   │
│  │ api.internal   192.168.1.10 → 10.0.0.50      │   │
│  └──────────────────────────────────────────────┘   │
│                                                     │
│  + ADDED                                            │
│  ┌──────────────────────────────────────────────┐   │
│  │ + 10.0.0.51  db.internal                     │   │
│  └──────────────────────────────────────────────┘   │
│                                                     │
│  − REMOVED                                          │
│  ┌──────────────────────────────────────────────┐   │
│  │ − 192.168.1.20  legacy.internal              │   │
│  └──────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────┤
│                        [Cancel]  [Apply Changes]    │
└─────────────────────────────────────────────────────┘
```

- **Modified** section always rendered first, with a `⚠` warning icon and yellow background. If a modified entry redirects a known public hostname (e.g. `github.com`, `microsoft.com`) to a private/loopback IP, the row background is red with additional text: "⚠ Public hostname redirected."
- **Added** rows: green left border, `+` prefix.
- **Removed** rows: red left border, `−` prefix.
- **Toggled** (enabled/disabled state change only): blue left border, shown after Added.
- Section headers are hidden if the section is empty.

### Behavior

- `[Apply Changes]` is the default button (Enter key).
- `[Cancel]` returns `DialogResult.Cancel` — caller aborts the switch.
- If `ProfileDiffResult.IsEmpty`: dialog is skipped entirely. Caller shows tray balloon: "Profile 'X' is already active — no changes."
- Dialog is non-blocking to the tray (it is shown as a foreground window but does not prevent tray interaction).

---

## Integration Points

Any caller that switches profiles must:

```csharp
var current = HostsFile.Instance.Entries;
var incoming = new HostsEntryList(File.ReadAllLines(targetPath), removeDefault: false);
var diff = ProfileDiff.Compute(current, incoming);

if (!diff.IsEmpty && Settings.Default.DiffBeforeSwitchEnabled)
{
    using var form = new DiffPreviewForm(targetProfileName, diff);
    if (form.ShowDialog() != DialogResult.OK)
        return; // user cancelled
}

HostsFile.Instance.Import(targetPath);
HostsFile.Instance.Save();
```

Callers: `ProfileSwitcher` (SPEC-01), `RollbackTimer` on revert (SPEC-03), `OnArchiveLoadClick` in `MainForm` (existing restore button — should also get diff preview).

---

## Security Note

The **Modified** section represents the highest-risk operation in this entire application. A hostname silently redirected to a new IP can:
- Break production services for the duration of the session.
- Send authenticated traffic to a wrong server (e.g. dev DB instead of prod).
- In multi-user environments, indicate a compromised hosts file.

The dialog must make this section impossible to miss — first, largest, yellow/red — not collapsed or hidden by default.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Target profile file is missing | Caller checks existence before calling `Compute`. If missing: error dialog, abort. |
| Target file is malformed | `HostsEntryList` parsing tolerates bad lines (marks them invalid). Diff still proceeds — invalid lines are excluded from comparison (same as current behavior). |
| Current hosts file modified externally since last load | Before `Import`, compare `File.GetLastWriteTimeUtc` against load time. If changed: warn "The hosts file was modified externally. Reload before switching?" with Yes/No. |
| Diff contains 100+ entries | Render all entries — no truncation. Dialog is scrollable. Performance: `Compute` runs in O(n) using `HashSet`. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/ProfileDiff.cs` | New — `Compute` method + `ProfileDiffResult` / `DiffPair` types |
| `src/DiffPreviewForm.cs` | New — modal dialog |
| `src/DiffPreviewForm.Designer.cs` | New — designer file |
| `src/MainForm.cs` | Wrap `OnArchiveLoadClick` with diff check |
| `src/Properties/Settings.settings` | `DiffBeforeSwitchEnabled : bool` (shared with SPEC-01) |
