using Equin.ApplicationFramework;

namespace HostsFileEditor.Controls;

/// <summary>
/// DataGridView class for use with HostsArchive objects.
/// </summary>
internal sealed class HostsArchiveDataGridView : DataGridView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostsArchiveDataGridView"/> class.
    /// </summary>
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
        ContextMenuStrip = menu;
    }

    private void OnProfileSettingsClick(object? sender, EventArgs e)
    {
        var archive = CurrentHostsArchive;
        if (archive == null) return;

        using var dlg = new ProfileSettingsForm(archive);
        dlg.ShowDialog(FindForm());
    }

    /// <summary>
    /// Gets the current hosts archive.
    /// </summary>
    public HostsArchive? CurrentHostsArchive
    {
        get
        {
            HostsArchive? archive = null;

            if (CurrentRow != null)
            {
                var view = CurrentRow.DataBoundItem as ObjectView<HostsArchive>;
                archive = view?.Object;
            }

            return archive;
        }
    }
}
