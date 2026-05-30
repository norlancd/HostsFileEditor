using Equin.ApplicationFramework;

namespace HostsFileEditor.Controls;

/// <summary>
/// DataGridView class for use with HostsArchive objects.
/// </summary>
internal sealed class HostsArchiveDataGridView : DataGridView
{
    private static readonly Color AutoBackupRowColor = Color.FromArgb(232, 242, 255);
    private static readonly Color AutoBackupHeaderColor = Color.FromArgb(210, 228, 255);
    private static readonly Font AutoBackupFont =
        new Font(SystemFonts.DefaultFont?.FontFamily ?? SystemFonts.MessageBoxFont!.FontFamily,
                 SystemFonts.DefaultFont?.Size ?? 9f, FontStyle.Italic);

    public HostsArchiveDataGridView()
    {
        AllowUserToResizeRows = false;
        AllowUserToResizeColumns = false;
        AllowUserToDeleteRows = false;
        AllowDrop = false;
        AllowUserToAddRows = false;
        AllowUserToOrderColumns = false;

        var menu = new ContextMenuStrip();

        var menuProfileSettings = new ToolStripMenuItem("Profile Settings…");
        menuProfileSettings.Click += OnProfileSettingsClick;
        menu.Items.Add(menuProfileSettings);

        var menuSaveAsProfile = new ToolStripMenuItem("Save as Profile…");
        menuSaveAsProfile.Click += OnSaveAsProfileClick;
        menu.Items.Add(menuSaveAsProfile);

        menu.Opening += OnContextMenuOpening;
        ContextMenuStrip = menu;

        CellFormatting += OnCellFormatting;
        RowPrePaint += OnRowPrePaint;
        SelectionChanged += OnSelectionChanged;
    }

    /// <summary>Gets the current hosts archive (null for auto-backup header rows).</summary>
    public HostsArchive? CurrentHostsArchive
    {
        get
        {
            if (CurrentRow == null) return null;
            var view = CurrentRow.DataBoundItem as ObjectView<HostsArchive>;
            return view?.Object;
        }
    }

    // ── Row styling ──────────────────────────────────────────────────────────

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= RowCount) return;
        var archive = ArchiveAt(e.RowIndex);
        if (archive == null || !archive.IsAutoBackup) return;

        // Show display name (formatted timestamp) instead of raw filename
        if (Columns[e.ColumnIndex].DataPropertyName == "FileName")
        {
            e.Value = archive.DisplayName;
            e.FormattingApplied = true;
        }
    }

    private void OnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= RowCount) return;
        var archive = ArchiveAt(e.RowIndex);
        if (archive == null || !archive.IsAutoBackup) return;

        var row = Rows[e.RowIndex];
        bool isFirstAutoBackup = e.RowIndex == 0 || ArchiveAt(e.RowIndex - 1)?.IsAutoBackup != true;

        if (isFirstAutoBackup)
        {
            // Draw a section-header band above this row using the row's own graphics
            var headerRect = new Rectangle(
                e.RowBounds.Left, e.RowBounds.Top,
                e.RowBounds.Width, Math.Min(18, e.RowBounds.Height));

            using var bg = new SolidBrush(AutoBackupHeaderColor);
            e.Graphics.FillRectangle(bg, headerRect);

            var autoBackupCount = Rows.Cast<DataGridViewRow>()
                .Count(r => r.Index >= 0 && ArchiveAt(r.Index)?.IsAutoBackup == true);

            var headerText = $"  ── Auto Backups ({autoBackupCount}) ──";
            using var font = new Font(Font, FontStyle.Bold);
            e.Graphics.DrawString(headerText, font, Brushes.DimGray,
                headerRect.Left, headerRect.Top + 2);
        }

        // Apply colour + italic to the data portion of the row
        foreach (DataGridViewCell cell in row.Cells)
        {
            cell.Style.BackColor = AutoBackupRowColor;
            cell.Style.Font = AutoBackupFont;
        }
    }

    // Prevent selecting auto-backup rows that serve as section headers
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        // Auto-backup rows are still selectable — they can be loaded/deleted
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var archive = CurrentHostsArchive;
        if (ContextMenuStrip == null) return;

        var isAutoBackup = archive?.IsAutoBackup == true;
        ContextMenuStrip.Items[0].Visible = !isAutoBackup;  // Profile Settings (user only)
        ContextMenuStrip.Items[1].Visible = isAutoBackup;   // Save as Profile  (auto only)
    }

    private void OnProfileSettingsClick(object? sender, EventArgs e)
    {
        var archive = CurrentHostsArchive;
        if (archive == null || archive.IsAutoBackup) return;

        using var dlg = new ProfileSettingsForm(archive);
        dlg.ShowDialog(FindForm());
    }

    private void OnSaveAsProfileClick(object? sender, EventArgs e)
    {
        var archive = CurrentHostsArchive;
        if (archive == null || !archive.IsAutoBackup) return;

        using var inputDialog = new InputForm();
        inputDialog.Text = "Save as Profile";
        inputDialog.Prompt = "Enter a name for the new profile:";

        if (inputDialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        var destPath = Path.Combine(HostsArchiveList.ArchiveDirectory, inputDialog.Input);
        try
        {
            Directory.CreateDirectory(HostsArchiveList.ArchiveDirectory);
            File.Copy(archive.FilePath, destPath, overwrite: false);
            HostsArchiveList.Instance.Add(new HostsArchive { FilePath = destPath });
        }
        catch (IOException ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private HostsArchive? ArchiveAt(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= RowCount) return null;
        var view = Rows[rowIndex].DataBoundItem as ObjectView<HostsArchive>;
        return view?.Object;
    }
}
