# SPEC-07 — IP Variables / Aliases

**Status:** Proposed  
**Priority:** P3  
**Depends on:** SPEC-09 (Audit Log — alias changes should be logged)*

---

## Objective

Define named IP aliases (e.g. `DEV_SERVER=192.168.1.10`) that multiple entries reference by name. Changing the alias value updates all referencing entries atomically in a single undo-able operation. This eliminates manual multi-row IP updates when a server's address changes.

---

## Current State

`HostsEntry.IpAddress` is a plain string, validated via `IPAddress.TryParse`. No abstraction layer exists between entry data and what is written to disk.

---

## Design Principle

Aliases are a **UI-layer abstraction only**. The hosts file on disk always contains the resolved IP address — never the alias name. The format `C:\Windows\System32\drivers\etc\hosts` must remain standards-compliant so any hosts file reader works correctly.

---

## Data Model

### `IpAlias`

```csharp
public record IpAlias
{
    public string Name  { get; init; }   // e.g. "DEV_SERVER" — alphanumeric + underscore, 1-32 chars
    public string Value { get; init; }   // valid IPv4 or IPv6, validated via IPAddress.TryParse
}
```

### `IpAliasStore` (new singleton)

Persisted at `%APPDATA%\HostsFileEditor\ip-aliases.json`.

```json
{
  "aliases": [
    { "name": "DEV_SERVER",   "value": "192.168.1.10" },
    { "name": "STAGING_DB",   "value": "10.0.0.50"    },
    { "name": "PROD_GATEWAY", "value": "203.0.113.1"  }
  ]
}
```

`IpAliasStore.Instance` exposes:
- `IReadOnlyList<IpAlias> Aliases`
- `string? Resolve(string name)` — returns the IP or null if alias not found
- `void Save(IpAlias alias)` — add or update
- `void Delete(string name)` — validates no entries reference it first

### `HostsEntry` Changes

`HostsEntry` gains an optional `AliasName : string?` property (in-memory only, not written to disk):

- When `AliasName` is set: `IpAddress` getter returns `IpAliasStore.Instance.Resolve(AliasName) ?? storedIpAddress`.
- `UnparsedText` always uses the resolved IP (same as today).
- `AliasName` is stored as a comment sentinel in the hosts file for persistence: `# [alias:DEV_SERVER]` appended to the entry's comment. Parsed back on load.

**Sentinel format in hosts file:**

```
192.168.1.10   api.local   # my api server [alias:DEV_SERVER]
192.168.1.10   db.local    # [alias:DEV_SERVER]
```

On parse: `HostsEntry` constructor detects `[alias:{name}]` in the comment, sets `AliasName`, strips the sentinel from `Comment`. On `UnparsedText` serialization: re-appends the sentinel. This keeps the file valid and round-trippable.

---

## Atomic Update

`IpAliasStore.UpdateAlias(string name, string newValue)`:

```csharp
public void UpdateAlias(string name, string newValue)
{
    ValidateIp(newValue);  // throws if invalid

    var affectedEntries = HostsFile.Instance.Entries
        .Where(e => e.AliasName == name)
        .ToList();

    UndoManager.Instance.BatchActions(() =>
    {
        // Update stored value in each entry
        foreach (var entry in affectedEntries)
            entry.SetIpFromAlias(newValue);  // internal, bypasses alias resolution

        // Update alias store
        var alias = Aliases.First(a => a.Name == name);
        Aliases = Aliases.Replace(alias, alias with { Value = newValue });
        PersistToJson();
    });

    HostsFile.Instance.Save();
}
```

This is a single `UndoManager` batch — one `Ctrl+Z` reverts all affected entries and the alias value together.

---

## UI

### IP Address Column Display

When `AliasName` is set, the IP Address cell shows: `192.168.1.10  [DEV_SERVER]`

- The alias name is rendered in a lighter color/italic to distinguish it from the IP.
- Custom `DataGridViewCell` paint override — the alias badge does not affect the editable text.
- Clicking the cell to edit: shows just the IP, not the alias badge. Editing the IP directly removes the alias binding for that entry.

### Alias Manager Dialog

Accessible from `Tools > Manage IP Aliases…`.

```
┌─────────────────────────────────────┐
│  IP Aliases                         │
├─────────────────────────────────────┤
│  Name          Value       Used By  │
│  DEV_SERVER    192.168.1.10   3     │
│  STAGING_DB    10.0.0.50      1     │
├─────────────────────────────────────┤
│  [+ Add]  [Edit]  [Delete]          │
└─────────────────────────────────────┘
```

- **Used By**: count of entries currently referencing this alias. Click count to jump to those entries in the grid.
- **Edit**: opens an inline edit for `Value`. Saving triggers `IpAliasStore.UpdateAlias` — shows a confirmation: "This will update 3 entries. Continue?"
- **Delete**: disabled if `Used By > 0`. User must remove alias references first (or "unbind all" confirmation option).

### Binding an Entry to an Alias

Right-click an entry row → "Bind IP to Alias…" → dropdown of existing aliases + "Create new alias…" option.

Binding replaces the entry's IP display with the alias badge but does not change the IP value or the hosts file immediately (it will use the current alias value on next save).

---

## Security Notes

- Aliases must not allow circular references (alias A resolves to alias B which resolves back to A). Circular detection: at `Save` time, resolve the full chain and reject if a cycle is detected.
- Aliases are global, not per-profile. A profile switch does not change alias values. If `DEV_SERVER` points to `192.168.1.10` and you switch to the "production" profile, entries bound to `DEV_SERVER` will still resolve to `192.168.1.10` — the alias is not environment-aware. This is by design (aliases represent infrastructure identity, not environment config) but must be documented clearly in the UI.
- Alias file (`%APPDATA%\...`) is user-writable. It is not used in any security decision — the hosts file written to disk always contains a literal IP validated by `IPAddress.TryParse`.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Alias deleted while entries reference it | `IpAliasStore.Delete` refuses if `Used By > 0`. User must unbind first via "Unbind all references" in the confirmation dialog. On unbind: `AliasName` is set to null, IP value remains the last resolved IP. |
| Alias value set to invalid IP | `IpAliasStore.Save` runs `IPAddress.TryParse`; throws on invalid. UI shows inline validation error. |
| Entry's `AliasName` refers to a non-existent alias | `Resolve` returns null. `IpAddress` getter falls back to the stored literal IP. Cell shows `192.168.1.10  [DEV_SERVER ⚠]` with a warning badge. |
| Profile loaded from disk with `[alias:X]` sentinel, but alias X not in alias store | Same as above — fallback to literal IP, warning badge. |
| Undo after alias update | Single `Ctrl+Z` reverts all affected entry IPs and the alias value simultaneously. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/IpAlias.cs` | New — record type |
| `src/IpAliasStore.cs` | New — singleton, JSON persistence |
| `src/HostsEntry.cs` | Add `AliasName`, sentinel parsing/serialization, `SetIpFromAlias()` |
| `src/Controls/HostsEntryDataGridView.cs` | Alias badge rendering in IP cell |
| `src/AliasManagerForm.cs` | New — alias CRUD dialog |
| `src/MainForm.cs` | Add "Manage IP Aliases…" to Tools menu |
