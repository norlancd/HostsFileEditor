using HostsFileEditor.Extensions;
using HostsFileEditor.Properties;
using HostsFileEditor.Utilities;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace HostsFileEditor;

public class HostsEntryList : BindingList<HostsEntry>
{
    public static readonly string[] DefaultLines = Resources.hosts.Split(
        [Environment.NewLine],
        StringSplitOptions.None);

    private readonly IUndoManager _undoManager;

    public IUndoManager UndoManager => _undoManager;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingList only used for basic add/remove/change notifications; PropertyDescriptor reflective paths not used.")]
    public HostsEntryList(IUndoManager undoManager, IEnumerable<string> entryLines, bool filterDefault)
        : this(undoManager)
    {
        AddLines(entryLines, filterDefault);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingList only used for basic add/remove/change notifications; PropertyDescriptor reflective paths not used.")]
    public HostsEntryList(IUndoManager undoManager)
    {
        _undoManager = undoManager;
        AllowEdit = true;
        AllowNew = true;
        AllowRemove = true;
        RaiseListChangedEvents = true;
    }

    public string Error => this.Any(entry => !entry.Valid) ? Resources.InvalidHostEntries : string.Empty;

    public void AddLines(IEnumerable<string> lines, bool removeDefault = true)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _undoManager.SuspendUndoRedo(() =>
        {
            var index = 0;
            foreach (var line in lines)
            {
                var isDefaultLine =
                    index < DefaultLines.Length &&
                    line.Trim() == DefaultLines[index++].Trim();

                if (!removeDefault || !isDefaultLine)
                {
                    Add(new HostsEntry(_undoManager, line));
                }
            }
        });
    }

    public void MoveBefore(IEnumerable<HostsEntry> entries, HostsEntry beforeEntry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(beforeEntry);

        this.BatchUpdate(() =>
        {
            var moving = entries.ToList();
            if (moving.Count == 0)
            {
                return;
            }

            // Capture original indices BEFORE any removals
            var originalIndices = moving.ToDictionary(e => e, e => IndexOf(e));
            var beforeIndex = IndexOf(beforeEntry);
            if (beforeIndex < 0)
            {
                return; // target not found
            }

            // Order moving entries by their original appearance in the list
            moving.Sort((x, y) => originalIndices[x].CompareTo(originalIndices[y]));

            // Compute insertion index after removals: shift left by how many moving items were before the target
            var removedBefore = moving.Count(e => originalIndices[e] < beforeIndex);
            var insertIndex = beforeIndex - removedBefore;
            if (insertIndex < 0) insertIndex = 0;

            _undoManager.BatchActions(() =>
            {
                // Remove using simple removal (one-by-one) to minimize re-ordering side effects
                foreach (var e in moving)
                {
                    // If already removed (duplicate in list not expected) skip
                    if (Contains(e)) base.Remove(e);
                }

                if (insertIndex > Count) insertIndex = Count;

                foreach (var entry in moving)
                {
                    Insert(insertIndex++, entry);
                }
            });
        });
    }

    public void MoveAfter(IEnumerable<HostsEntry> entries, HostsEntry afterEntry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(afterEntry);

        this.BatchUpdate(() =>
        {
            var moving = entries.ToList();
            if (moving.Count == 0)
            {
                return;
            }

            var originalIndices = moving.ToDictionary(e => e, e => IndexOf(e));
            var afterIndex = IndexOf(afterEntry);
            if (afterIndex < 0)
            {
                return; // target not found
            }

            moving.Sort((x, y) => originalIndices[x].CompareTo(originalIndices[y]));

            var removedBefore = moving.Count(e => originalIndices[e] < afterIndex);
            var updatedAfterIndex = afterIndex - removedBefore; // index after removals
            var insertIndex = updatedAfterIndex + 1; // after the target

            _undoManager.BatchActions(() =>
            {
                foreach (var e in moving)
                {
                    if (Contains(e)) base.Remove(e);
                }

                if (insertIndex > Count) insertIndex = Count;
                if (insertIndex < 0) insertIndex = 0;

                foreach (var entry in moving)
                {
                    Insert(insertIndex++, entry);
                }
            });
        });
    }

    public void InsertBefore(HostsEntry entry, HostsEntry? newEntry = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var insertIndex = IndexOf(entry);
        Insert(insertIndex, newEntry ?? new HostsEntry(_undoManager));
    }

    public void InsertAfter(HostsEntry entry, HostsEntry? newEntry = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var insertIndex = IndexOf(entry) + 1;
        Insert(insertIndex, newEntry ?? new HostsEntry(_undoManager));
    }

    public void Insert(HostsEntry entry, IEnumerable<HostsEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entries);

        var insertIndex = IndexOf(entry);

        _undoManager.BatchActions(() =>
        {
            foreach (var newEntry in entries.ToList())
            {
                Insert(insertIndex++, newEntry);
            }
        });
    }

    public void Remove(IEnumerable<HostsEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        this.BatchUpdate(() =>
        {
            _undoManager.BatchActions(() =>
            {
                foreach (var entry in entries.ToList())
                {
                    Remove(entry);
                }
            });
        });
    }

    public void Add() => Add(new HostsEntry(_undoManager));

    public void SetEnabled(IEnumerable<HostsEntry> entries, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(entries);

        this.BatchUpdate(() =>
        {
            _undoManager.BatchActions(() =>
            {
                foreach (var entry in entries)
                {
                    entry.Enabled = isEnabled;
                }
            });
        });
    }

    protected override object AddNewCore() => new HostsEntry(_undoManager, string.Empty);

    protected override void InsertItem(int index, HostsEntry item)
    {
        _undoManager.AddActions(
            undoAction: () => Remove(item),
            redoAction: () => Insert(index, item));

        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];

        _undoManager.AddActions(
            undoAction: () => Insert(index, item),
            redoAction: () => Remove(item));

        base.RemoveItem(index);
    }
}
