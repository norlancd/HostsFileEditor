# SPEC-12 — Entry Groups / Categories

**Status:** Proposed  
**Priority:** P5  
**Depends on:** *(none — purely additive UI feature)*

---

## Objective

Allow visual grouping of entries within a profile using colored section headers, making large hosts files easier to scan and manage. Groups organize entries by project, environment, or purpose (e.g. "Development APIs", "Ad Blocking", "Database Servers") without affecting DNS resolution behavior.

---

## Current State

Entries have a `Comment` field. Users can add `# --- section name ---` lines manually as comment-only entries, but they have no special rendering, no color, and no UI for management.

---

## Format: Backward-Compatible Sentinel Encoding

Groups are encoded as comment lines in the hosts file using a sentinel prefix. This means:
- Any hosts file reader that does not understand groups sees them as ordinary comments.
- The format is round-trippable: load → parse → save produces identical group structure.
- Existing comment-only entries (`HostsEntry.HasCommentOnly == true`) are not affected.

**Sentinel format:**

```
# [GROUP:Development APIs:#4A90D9]
127.0.0.1   api.local      # local dev
192.168.1.10  db.local
# [/GROUP]
```

Group name: free text, max 60 characters. No nesting allowed.
Color: hex RGB `#RRGGBB`. Default: `#808080` (gray) if omitted.

**Parsing rule in `HostsEntryList.AddLines`:**

- Line matching `# [GROUP:{name}:{color}]` → create `HostsGroup` and assign to subsequent entries.
- Line matching `# [/GROUP]` → clear current group.
- All other lines → parsed as before.
- A `HostsGroup` sentinel line itself is not added as a `HostsEntry` — it is stored separately in a `Groups` collection on `HostsEntryList`.

---

## Data Model

### `HostsGroup`

```csharp
public class HostsGroup
{
    public string Name  { get; set; }   // display name
    public Color  Color { get; set; }   // header background color
    public int    StartIndex { get; }   // index in HostsEntryList of first member entry
    public int    EndIndex   { get; }   // index of last member entry (inclusive)
}
```

`HostsEntryList` gains:
- `IReadOnlyList<HostsGroup> Groups` — computed after any `ListChanged`, based on sentinel lines.
- `HostsGroup? GetGroup(HostsEntry entry)` — returns the group containing the entry, or null.

`HostsEntry` is not changed — group membership is determined by position relative to sentinel lines, not stored on the entry.

### Serialization

`HostsFile.SaveAs` iterates `Entries` and inserts sentinel lines at group boundaries before writing. The sentinel lines are generated from `HostsEntryList.Groups` — they are not stored as `HostsEntry` objects in the list.

---

## UI: Group Header Rows

`HostsEntryDataGridView` custom row painting:

- Before the first entry of each group, render a **non-editable header row** spanning all columns:
  - Background: group color (muted, e.g. 40% opacity over the grid background).
  - Text: group name, bold, centered.
  - Height: 22px (slightly taller than data rows).
  - Not selectable, not part of the data source — rendered as a visual separator only (custom `RowPrePaint` with owner draw for the header position).

Implementation approach: use `DataGridView.Rows` custom row heights and a `Dictionary<int, HostsGroup>` mapping row display index → group header. Repopulated on `HostsEntryList.ListChanged`.

### Context Menu Additions

**Right-click on a regular entry row:**
- "Add Group Before This Entry…" → opens `GroupEditForm` with name + color picker. Creates a new group starting at that entry.
- "Move to Group →" → submenu of existing groups + "No Group" option.

**Right-click on a group header row:**
- "Rename Group…"
- "Change Color…"
- "Select All Entries in Group"
- "Dissolve Group" (removes the header/footer sentinels, entries remain unchanged)
- "Delete Group and All Entries" (with confirmation)

---

## `GroupEditForm`

Small dialog (300×180):

```
┌────────────────────────────────┐
│  Group Name:  [____________]   │
│  Color:       [████ #4A90D9]   │
│                                │
│               [Cancel]  [Save] │
└────────────────────────────────┘
```

Color picker: a row of 8 preset swatches + "Custom…" option (opens `ColorDialog`).

---

## Drag-and-Drop Behavior

Existing drag-and-drop entry reordering (via `HostsEntryDataGridView`) must respect group boundaries:

- Dragging an entry **within** its group: allowed, reorders within the group.
- Dragging an entry **out of** its group: allowed, but the entry loses its group membership (moved before the group header or after the group footer).
- Dragging a group header row: moves the entire group (all entries between the sentinels) to the new position. Not implemented in the initial version — group header rows are not draggable in v1.

---

## Interaction with Other Features

- **Filter** (SPEC-01 existing): groups headers are always shown even if all their entries are filtered out. This prevents the filter from making groups invisible and confusing the user about structure.
- **Conflict Detection** (SPEC-06): conflicts within a group are shown normally. The conflict panel shows the group name alongside the entry reference.
- **URL Import** (SPEC-10): imported entries are placed in a group named after the import source label (e.g. "StevenBlack Unified"), created automatically if it doesn't exist. On re-import, the group is updated in place.
- **Archive/Profile switch** (SPEC-01): groups are part of the hosts file content via sentinels, so they are automatically saved and restored with every profile. No special handling needed.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Entry moved via up/down arrows past a group boundary | Entry leaves the group (sentinel not moved). Group boundaries are defined by file position. |
| Two adjacent groups with no entries between them | Valid structure. Both headers are rendered back-to-back. |
| Group sentinel without a matching `[/GROUP]` | Parsed as an open-ended group: all entries from the sentinel to end-of-file are in the group. On next save, `[/GROUP]` is written before EOF. |
| Group name contains `]` character | Escaped as `\]` in the sentinel. Parser handles escape. |
| File with no groups | `Groups` list is empty. No headers rendered. No behavior change. |
| Undo a "Delete Group and All Entries" | All entries in the batch are undo-registered via `UndoManager.BatchActions`. Single `Ctrl+Z` restores all. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/HostsGroup.cs` | New — group model |
| `src/HostsEntryList.cs` | Add `Groups` property, sentinel parsing, sentinel injection in `SaveAs` |
| `src/HostsFile.cs` | Pass groups to `SaveAs` for sentinel injection |
| `src/Controls/HostsEntryDataGridView.cs` | Group header row rendering, context menu additions |
| `src/GroupEditForm.cs` | New — name + color picker dialog |
