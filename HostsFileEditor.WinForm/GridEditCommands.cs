using HostsFileEditor.Controls;
using System.Text;

namespace HostsFileEditor;

/// <summary>
/// Owns the row-level editing commands for the entries grid — Copy/Cut/Paste,
/// Delete, Duplicate, Move Up/Down, Insert Above/Below, Check/Uncheck. Extracted
/// out of MainForm so this concern can be reasoned about independently of
/// profile/audit logic living in the rest of the form (C1).
/// </summary>
internal sealed class GridEditCommands
{
    private readonly Form _owner;
    private readonly HostsEntryDataGridView _grid;
    private readonly IAuditLogger _auditLogger;
    private readonly ToolStripMenuItem _menuCopy;
    private readonly ToolStripMenuItem _menuContextCopy;
    private readonly ToolStripMenuItem _menuCut;
    private readonly ToolStripMenuItem _menuContextCut;
    private readonly ToolStripMenuItem _menuPaste;
    private readonly ToolStripMenuItem _menuContextPaste;

    private IEnumerable<HostsEntry>? _clipboardEntries;

    public GridEditCommands(
        Form owner,
        HostsEntryDataGridView grid,
        IAuditLogger auditLogger,
        ToolStripMenuItem menuCopy, ToolStripMenuItem menuContextCopy,
        ToolStripMenuItem menuCut, ToolStripMenuItem menuContextCut,
        ToolStripMenuItem menuPaste, ToolStripMenuItem menuContextPaste)
    {
        _owner = owner;
        _grid = grid;
        _auditLogger = auditLogger;
        _menuCopy = menuCopy;
        _menuContextCopy = menuContextCopy;
        _menuCut = menuCut;
        _menuContextCut = menuContextCut;
        _menuPaste = menuPaste;
        _menuContextPaste = menuContextPaste;
    }

    public void Copy(object sender, EventArgs e)
    {
        // HACK: If editing cell forward cut/copy/paste command
        // to editing control
        if (_grid.IsCurrentCellInEditMode)
        {
            var keys = _menuCopy.ShortcutKeys;
            _menuCopy.ShortcutKeys = Keys.None;
            _menuContextCopy.ShortcutKeys = Keys.None;
            SendKeys.SendWait("^(C)");
            _menuCopy.ShortcutKeys = keys;
            _menuContextCopy.ShortcutKeys = keys;
            return;
        }

        if (_grid.SelectedRows.Count > 0)
        {
            _clipboardEntries = [.. _grid.SelectedHostEntries.Select(entry => new HostsEntry(entry))];
        }
        else
        {
            StringBuilder builder = new();

            foreach (DataGridViewCell cell in _grid.SelectedCells)
            {
                if (cell.ValueType == typeof(string))
                {
                    builder.Append(cell.Value?.ToString());
                }
            }

            Clipboard.SetText(builder.ToString());
        }
    }

    public void Cut(object sender, EventArgs e)
    {
        // HACK: If editing cell forward cut/copy/paste command
        // to editing control
        if (_grid.IsCurrentCellInEditMode)
        {
            var keys = _menuCut.ShortcutKeys;
            _menuCut.ShortcutKeys = Keys.None;
            _menuContextCut.ShortcutKeys = Keys.None;
            SendKeys.SendWait("^(X)");
            _menuCut.ShortcutKeys = keys;
            _menuContextCut.ShortcutKeys = keys;
            return;
        }

        _grid.CancelEdit();

        if (_grid.SelectedRows.Count > 0)
        {
            _clipboardEntries = [.. _grid.SelectedHostEntries];
            var snapshots = _clipboardEntries.Select(MainForm.ToSnapshot).ToList();

            HostsFile.Instance.Entries.Remove(_clipboardEntries);
            foreach (var snap in snapshots)
            {
                _auditLogger.Log(AuditActionType.EntryRemoved, AuditSource.MainForm, new AuditDetail { Entry = snap });
            }
        }
        else
        {
            StringBuilder builder = new();

            foreach (DataGridViewCell cell in _grid.SelectedCells)
            {
                if (cell.ValueType == typeof(string))
                {
                    builder.Append(cell.Value?.ToString());
                    cell.Value = string.Empty;
                }
            }

            Clipboard.SetText(builder.ToString());
        }
    }

    public void Paste(object sender, EventArgs e)
    {
        // HACK: If editing cell forward cut/copy/paste command
        // to editing control
        if (_grid.IsCurrentCellInEditMode)
        {
            var keys = _menuPaste.ShortcutKeys;
            _menuPaste.ShortcutKeys = Keys.None;
            _menuContextPaste.ShortcutKeys = Keys.None;
            SendKeys.SendWait("^(V)");
            _menuPaste.ShortcutKeys = keys;
            _menuContextPaste.ShortcutKeys = keys;
            return;
        }

        _grid.CancelEdit();

        if (_grid.SelectedRows.Count > 0 && _clipboardEntries != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.Insert(currentEntry, _clipboardEntries);
            }

            _clipboardEntries = null;
        }
        else
        {
            foreach (DataGridViewCell cell in _grid.SelectedCells)
            {
                if (cell.ValueType == typeof(string))
                {
                    cell.Value = Clipboard.GetText();
                }
            }
        }
    }

    public void Delete(object sender, EventArgs e)
    {
        List<HostsEntry> entries;
        if (_grid.SelectedRows.Count > 0)
        {
            entries = _grid.SelectedHostEntries.ToList();
        }
        else if (_grid.CurrentHostEntry != null)
        {
            // Clearing a single cell's text is never useful on its own — it just
            // leaves a half-blank row — so this always acts on the whole row instead.
            entries = [_grid.CurrentHostEntry];
        }
        else
        {
            return;
        }

        var message = entries.Count == 1
            ? $"Delete this entry?\n\n{entries[0].UnparsedText}"
            : $"Delete these {entries.Count} entries?";

        var result = MessageBox.Show(_owner, message, _owner.Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        var snapshots = entries.Select(MainForm.ToSnapshot).ToList();
        HostsFile.Instance.Entries.Remove(entries);
        foreach (var snap in snapshots)
        {
            _auditLogger.Log(AuditActionType.EntryRemoved, AuditSource.MainForm, new AuditDetail { Entry = snap });
        }
    }

    public void Duplicate(object sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count > 0)
        {
            foreach (var entry in _grid.SelectedHostEntries)
            {
                HostsFile.Instance.Entries.InsertAfter(entry, new HostsEntry(entry));
            }
        }
        else if (_grid.CurrentRow?.DataBoundItem != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.InsertAfter(currentEntry, new HostsEntry(currentEntry));
            }
        }
    }

    public void MoveUp(object sender, EventArgs e)
    {
        var entries = HostsFile.Instance.Entries;

        if (_grid.SelectedRows.Count > 0)
        {
            var selectedEntries = _grid.SelectedHostEntries.ToList();
            var firstSelected = _grid.FirstSelectedHostEntry;

            // The block moves up by one position relative to whatever currently
            // sits right before it — NOT relative to one of its own members (using
            // the selection's own last item as the target was always a no-op).
            var firstIndex = firstSelected != null ? entries.IndexOf(firstSelected) : -1;
            if (firstIndex > 0)
            {
                entries.MoveBefore(selectedEntries, entries[firstIndex - 1]);
                _grid.SelectedHostEntries = selectedEntries;
            }
        }
        else if (_grid.CurrentHostEntry != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            var currentIndex = entries.IndexOf(currentEntry);
            if (currentIndex > 0)
            {
                entries.MoveBefore([currentEntry], entries[currentIndex - 1]);
                _grid.SelectedHostEntries = [currentEntry];
            }
        }
    }

    public void MoveDown(object sender, EventArgs e)
    {
        var entries = HostsFile.Instance.Entries;

        if (_grid.SelectedRows.Count > 0)
        {
            var selectedEntries = _grid.SelectedHostEntries.ToList();
            var lastSelected = _grid.LastSelectedHostEntry;

            // The block moves down by one position relative to whatever currently
            // sits right after it — NOT relative to one of its own members (using
            // the selection's own last item as the target was always a no-op).
            var lastIndex = lastSelected != null ? entries.IndexOf(lastSelected) : -1;
            if (lastIndex >= 0 && lastIndex < entries.Count - 1)
            {
                entries.MoveAfter(selectedEntries, entries[lastIndex + 1]);
                _grid.SelectedHostEntries = selectedEntries;
            }
        }
        else if (_grid.CurrentHostEntry != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            var currentIndex = entries.IndexOf(currentEntry);
            if (currentIndex >= 0 && currentIndex < entries.Count - 1)
            {
                entries.MoveAfter([currentEntry], entries[currentIndex + 1]);
                _grid.SelectedHostEntries = [currentEntry];
            }
        }
    }

    public void InsertAbove(object sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.InsertBefore(currentEntry);
            }
        }
        else
        {
            _grid.CancelEdit();
            HostsFile.Instance.Entries.Add();
        }
    }

    public void InsertBelow(object sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.InsertAfter(currentEntry);
            }
        }
        else
        {
            _grid.CancelEdit();
            HostsFile.Instance.Entries.Add();
        }
    }

    public void Check(object sender, EventArgs e)
    {
        _grid.CancelEdit();

        if (_grid.SelectedRows.Count > 0)
        {
            HostsFile.Instance.Entries.SetEnabled(_grid.SelectedHostEntries, isEnabled: true);
        }
        else if (_grid.CurrentHostEntry != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.SetEnabled([currentEntry], isEnabled: true);
            }
        }
    }

    public void Uncheck(object sender, EventArgs e)
    {
        _grid.CancelEdit();

        if (_grid.SelectedRows.Count > 0)
        {
            HostsFile.Instance.Entries.SetEnabled(_grid.SelectedHostEntries, isEnabled: false);
        }
        else if (_grid.CurrentHostEntry != null)
        {
            var currentEntry = _grid.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.SetEnabled([currentEntry], isEnabled: false);
            }
        }
    }
}
