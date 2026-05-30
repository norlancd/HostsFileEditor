using Equin.ApplicationFramework;
using HostsFileEditor.Extensions;
using HostsFileEditor.Properties;
using HostsFileEditor.Utilities;
using System.Text;

namespace HostsFileEditor;

/// <summary>
/// The main form for the application.
/// </summary>
internal partial class MainForm : Form
{
    /// <summary>
    /// The filter.
    /// </summary>
    private HostsFilter? _filter;

    /// <summary>
    /// The host entries view.
    /// </summary>
    private BindingListView<HostsEntry>? _hostEntriesView;

    /// <summary>
    /// The hosts archive view.
    /// </summary>
    private BindingListView<HostsArchive>? _hostsArchiveView;

    /// <summary>
    /// The clipboard host entries.
    /// </summary>
    private IEnumerable<HostsEntry>? _clipboardEntries;

    /// <summary>
    /// Determines if user is currently adding a new row.  Used for ugly
    /// hacks setup in load event.
    /// </summary>
    private bool _addingNew;

    /// <summary>
    /// Ignore adding new in progress. Used for ugly hacks setup in load 
    /// event.
    /// </summary>
    private bool _ignoreAddingNew;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    public MainForm()
    {
        InitializeComponent();

        saveFileDialog.InitialDirectory = HostsFile.DefaultHostFilePath;

        // Prevent data binding from setting properties to null when
        // an empty string is typed in
        columnComment.DefaultCellStyle.NullValue = null;
        columnIpAddress.DefaultCellStyle.NullValue = null;
        columnHostnames.DefaultCellStyle.NullValue = null;
    }

    private const int WmHotkey = 0x0312;

    private ToolStripMenuItem? _menuTrayProfiles;
    private ToolStripMenuItem? _menuBarProfiles;

    /// <inheritdoc />
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ProgramSingleInstance.WmShowFirstInstance)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            this.ShowOrActivate();
        }
        else if (message.Msg == WmHotkey)
        {
            int id = message.WParam.ToInt32();
            var profile = HotkeyRegistry.GetProfileById(id);
            if (profile != null)
                ProfileSwitcher.Activate(profile, ProfileSwitcher.TriggerSource.TrayHotkey);
        }

        base.WndProc(ref message);
    }

    /// <summary>
    /// Called when archive clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance
    /// containing the event data.</param>
    private void OnArchiveClick(object sender, EventArgs e)
    {
        dataGridViewHostsEntries.CommitEdit(
            DataGridViewDataErrorContexts.Commit);

        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Resources.InputArchivePrompt;

        var result = inputDialog.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            HostsFile.Instance.Archive(inputDialog.Input);
        }
    }

    /// <summary>
    /// Occurs when copy clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnCopyClick(object sender, EventArgs e)
    {
        // HACK: If editing cell forward cut/copy/paste command
        // to editing control
        if (dataGridViewHostsEntries.IsCurrentCellInEditMode)
        {
            var keys = menuCopy.ShortcutKeys;
            menuCopy.ShortcutKeys = Keys.None;
            menuContextCopy.ShortcutKeys = Keys.None;
            SendKeys.SendWait("^(C)");
            menuCopy.ShortcutKeys = keys;
            menuContextCopy.ShortcutKeys = keys;
            return;
        }

        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            _clipboardEntries = [.. dataGridViewHostsEntries.SelectedHostEntries.Select(entry => new HostsEntry(entry))];
        }
        else
        {
            StringBuilder builder = new();

            foreach (
                DataGridViewCell cell in
                dataGridViewHostsEntries.SelectedCells)
            {
                if (cell.ValueType == typeof(string))
                {
                    builder.Append(cell.Value?.ToString());
                }
            }

            Clipboard.SetText(builder.ToString());
        }
    }

    /// <summary>
    /// Occurs when cut clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnCutClick(object sender, EventArgs e)
    {
        // HACK: If editing cell forward cut/copy/paste command
        // to editing control
        if (dataGridViewHostsEntries.IsCurrentCellInEditMode)
        {
            var keys = menuCut.ShortcutKeys;
            menuCut.ShortcutKeys = Keys.None;
            menuContextCut.ShortcutKeys = Keys.None;
            SendKeys.SendWait("^(X)");
            menuCut.ShortcutKeys = keys;
            menuContextCut.ShortcutKeys = keys;
            return;
        }

        dataGridViewHostsEntries.CancelEdit();

        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            _clipboardEntries = [.. dataGridViewHostsEntries.SelectedHostEntries];
            var snapshots = _clipboardEntries.Select(ToSnapshot).ToList();

            HostsFile.Instance.Entries.Remove(_clipboardEntries);
            foreach (var snap in snapshots)
            {
                AuditLogger.Instance.Log(new AuditEntry
                {
                    Action = nameof(AuditActionType.EntryRemoved),
                    Source = AuditSource.MainForm,
                    Detail = new AuditDetail { Entry = snap }
                });
            }
        }
        else
        {
            StringBuilder builder = new();

            foreach (
                DataGridViewCell cell in
                dataGridViewHostsEntries.SelectedCells)
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

    /// <summary>
    /// Occurs when delete clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnDeleteClick(object sender, EventArgs e)
    {
        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            var entries = dataGridViewHostsEntries.SelectedHostEntries.ToList();
            var snapshots = entries.Select(ToSnapshot).ToList();
            HostsFile.Instance.Entries.Remove(entries);
            foreach (var snap in snapshots)
            {
                AuditLogger.Instance.Log(new AuditEntry
                {
                    Action = nameof(AuditActionType.EntryRemoved),
                    Source = AuditSource.MainForm,
                    Detail = new AuditDetail { Entry = snap }
                });
            }
        }
        else
        {
            foreach (
                DataGridViewCell cell in
                dataGridViewHostsEntries.SelectedCells)
            {
                if (cell.ValueType == typeof(string))
                {
                    cell.Value = string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// Occurs when duplicate clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnDuplicateClick(object sender, EventArgs e)
    {
        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            foreach (var entry in dataGridViewHostsEntries.SelectedHostEntries)
            {
                HostsFile.Instance.Entries.InsertAfter(entry, new HostsEntry(entry));
            }
        }
        else if (dataGridViewHostsEntries.CurrentRow?.DataBoundItem != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.InsertAfter(
                    currentEntry,
                    new HostsEntry(currentEntry));
            }
        }
    }

    /// <summary>
    /// Occurs when disable hosts clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnDisableHostsClick(object sender, EventArgs e)
    {
        bool checkState = (sender as dynamic).Checked;

        menuDisable.Checked = !checkState;
        buttonDisable.Checked = !checkState;
        menuContextDisable.Checked = !checkState;

        if (checkState)
        {
            HostsFile.EnableHostsFile();
            AuditLogger.Instance.Log(new AuditEntry
            {
                Action = nameof(AuditActionType.HostsFileEnabled),
                Source = AuditSource.MainForm
            });
        }
        else
        {
            HostsFile.DisableHostsFile();
            AuditLogger.Instance.Log(new AuditEntry
            {
                Action = nameof(AuditActionType.HostsFileDisabled),
                Source = AuditSource.MainForm
            });
        }

        UpdateNotifyIcon();
    }

    /// <summary>
    /// The on edit click.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnEditClick(object sender, EventArgs e) => this.ShowOrActivate();

    /// <summary>
    /// Occurs when filter comment clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnFilterCommentClick(object sender, EventArgs e)
    {
        bool checkState = (sender as dynamic).Checked;

        if (_filter != null)
        {
            _filter.Comments = !checkState;
        }

        menuFilterComments.Checked = !checkState;
        buttonFilterComment.Checked = !checkState;

        _hostEntriesView?.Refresh();
    }

    /// <summary>
    /// Occurs when filter disabled clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnFilterDisabledClick(object sender, EventArgs e)
    {
        bool checkState = (sender as dynamic).Checked;

        if (_filter != null)
        {
            _filter.Disabled = !checkState;
        }

        menuFilterDisabled.Checked = !checkState;
        buttonFilterDisabled.Checked = !checkState;

        _hostEntriesView?.Refresh();
    }

    /// <summary>
    /// Occurs when filter text changed.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnFilterTextChanged(object sender, EventArgs e) => _hostEntriesView?.Refresh();

    /// <summary>
    /// Occurs when form loads.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnFomLoad(object sender, EventArgs e)
    {
        LoadSettings();

        _hostsArchiveView = new BindingListView<HostsArchive>(components)
        {
            DataSource = HostsArchiveList.Instance
        };

        bindingSourceArchive.DataSource = _hostsArchiveView;
        _hostsArchiveView.Sort = nameof(HostsArchive.FileName);

        bindingSourceHostFile.DataSource = HostsFile.Instance;

        _hostEntriesView = new BindingListView<HostsEntry>(components)
        {
            DataSource = HostsFile.Instance.Entries
        };

        _hostEntriesView.AddingNew += (s, args) => args.NewObject = new HostsEntry(string.Empty);

        // Tell grid how to clear sort of underlying data source
        // since it doesn't know how by itself
        dataGridViewHostsEntries.ClearSort = () =>
        {
            _hostEntriesView.RemoveSort();
        };

        bindingSourceView.DataSource = _hostEntriesView;

        _filter = new HostsFilter(
                hostEntry => hostEntry.ToString().Contains(textFilter.Text));

        _hostEntriesView.Filter = _filter;

        HostsFile.Instance.Entries.ResetBindings();

        menuDisable.Checked = !HostsFile.IsEnabled;
        buttonDisable.Checked = !HostsFile.IsEnabled;

        UpdateNotifyIcon();
        InitializeHotkeySupport();
        SetupAuditLoggerNotifications();
        TakeBaselineSnapshot();

        // Insert "View Audit Log…" just before the Exit item
        int exitIndex = menuFile.DropDownItems.IndexOf(menuExit);
        var menuViewAuditLog = new ToolStripMenuItem("View Audit Log…");
        menuViewAuditLog.Click += OnViewAuditLogClick;
        menuFile.DropDownItems.Insert(exitIndex, menuViewAuditLog);
        menuFile.DropDownItems.Insert(exitIndex, new ToolStripSeparator());

        // HACK: Make sure a newly added row gets committed after
        // the first cell is validated so HostsEntry validation and data
        // binding behaves correctly
        _hostEntriesView.AddingNew +=
            (sender1, e1) =>
            {
                _addingNew = true;
            };

        dataGridViewHostsEntries.CellValidated +=
            (sender1, e1) =>
            {
                if (!_ignoreAddingNew && _addingNew)
                {
                    _hostEntriesView.EndNew(_hostEntriesView.Count - 1);
                    _addingNew = false;
                }
            };

        dataGridViewHostsEntries.CurrentCellChanged +=
            (sender1, e1) =>
            {
                _ignoreAddingNew = true;
            };

        dataGridViewHostsEntries.CurrentCellDirtyStateChanged +=
            (sender1, e1) =>
            {
                _ignoreAddingNew = false;
            };
    }


    /// <summary>
    /// Occurs when form is closing.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.ApplicationExitCall)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>
    /// Occurs when form shown for first time.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnFormShown(object sender, EventArgs e)
    {
        dataGridViewHostsEntries.AutoResizeColumn(
            columnEnabled.Index,
            DataGridViewAutoSizeColumnMode.AllCells);

        dataGridViewHostsEntries.AutoResizeColumn(
            columnIpAddress.Index,
            DataGridViewAutoSizeColumnMode.AllCells);

        // Add room for error provider
        columnIpAddress.Width += 20;

        dataGridViewHostsEntries.AutoResizeColumn(
            columnHostnames.Index,
            DataGridViewAutoSizeColumnMode.AllCells);

        // HACK: calling focus causes cell validate to occur
        // which causes row to be committed
        _ignoreAddingNew = true;

        textFilter.Focus();

        // Deselect top left cell for aesthetics
        foreach (DataGridViewCell cell in dataGridViewHostsEntries.SelectedCells)
        {
            cell.Selected = false;
        }

        _ignoreAddingNew = false;
    }

    /// <summary>
    /// Occurs when import clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnImportClick(object sender, EventArgs e)
    {
        var result = openFileDialog.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            HostsFile.Instance.Import(openFileDialog.FileName);
            _currentProfileName = Path.GetFileName(openFileDialog.FileName);
            AuditLogger.Instance.Log(new AuditEntry
            {
                Action = nameof(AuditActionType.FileImported),
                Source = AuditSource.MainForm,
                Detail = new AuditDetail { SourcePath = openFileDialog.FileName }
            });
        }
    }

    /// <summary>
    /// Occurs when exit clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnExitClick(object sender, EventArgs e)
    {
        SaveSettings();
        Application.Exit();
    }

    /// <summary>
    /// Occurs when restore clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnRestoreClick(object sender, EventArgs e)
    {
        HostsFile.Instance.RestoreDefault();
        _currentProfileName = "current";
        AuditLogger.Instance.Log(new AuditEntry
        {
            Action = nameof(AuditActionType.DefaultRestored),
            Source = AuditSource.MainForm
        });
    }

    /// <summary>
    /// Occurs when save as clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnSaveAsClick(object sender, EventArgs e)
    {
        dataGridViewHostsEntries.CommitEdit(
            DataGridViewDataErrorContexts.Commit);

        var result = saveFileDialog.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            HostsFile.Instance.SaveAs(saveFileDialog.FileName);
        }
    }

    /// <summary>
    /// Occurs when save clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnSaveClick(object sender, EventArgs e)
    {
        dataGridViewHostsEntries.CommitEdit(
            DataGridViewDataErrorContexts.Commit);

        LogSaveChanges();
        HostsFile.Instance.Save();
    }

    /// <summary>
    /// Occurs when move down clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnMoveDownClick(object sender, EventArgs e)
    {
        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            var selectedEntries = dataGridViewHostsEntries.SelectedHostEntries.ToList();
            var firstSelected = dataGridViewHostsEntries.FirstSelectedHostEntry;

            if (firstSelected != null)
            {
                HostsFile.Instance.Entries.MoveAfter(
                    dataGridViewHostsEntries.SelectedHostEntries,
                    firstSelected);

                dataGridViewHostsEntries.SelectedHostEntries = selectedEntries;
            }
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            var selectedEntries = new List<HostsEntry>([currentEntry]);

            HostsFile.Instance.Entries.MoveAfter([currentEntry], currentEntry);

            dataGridViewHostsEntries.SelectedHostEntries = selectedEntries;
        }
    }

    /// <summary>
    /// Occurs when move up clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnMoveUpClick(object sender, EventArgs e)
    {
        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            var selectedEntries = dataGridViewHostsEntries.SelectedHostEntries.ToList();
            var lastSelected = dataGridViewHostsEntries.LastSelectedHostEntry;

            if (lastSelected != null)
            {
                HostsFile.Instance.Entries.MoveBefore(
                     dataGridViewHostsEntries.SelectedHostEntries,
                     lastSelected);

                dataGridViewHostsEntries.SelectedHostEntries = selectedEntries;
            }
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            var selectedEntries = new List<HostsEntry>([currentEntry]);

            HostsFile.Instance.Entries.MoveBefore([currentEntry], currentEntry);

            dataGridViewHostsEntries.SelectedHostEntries = selectedEntries;
        }
    }

    /// <summary>
    /// The on notify icon double click.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnNotifyIconDoubleClick(object sender, EventArgs e) => this.ShowOrActivate();

    /// <summary>
    /// Occurs when paste clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnPasteClick(object sender, EventArgs e)
    {
        // HACK: If editing cell forward cut/copy/paste command
        // to editing control
        if (dataGridViewHostsEntries.IsCurrentCellInEditMode)
        {
            var keys = menuPaste.ShortcutKeys;
            menuPaste.ShortcutKeys = Keys.None;
            menuContextPaste.ShortcutKeys = Keys.None;
            SendKeys.SendWait("^(V)");
            menuPaste.ShortcutKeys = keys;
            menuContextPaste.ShortcutKeys = keys;
            return;
        }

        dataGridViewHostsEntries.CancelEdit();

        if (dataGridViewHostsEntries.SelectedRows.Count > 0 &&
            _clipboardEntries != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.Insert(currentEntry, _clipboardEntries);
            }

            _clipboardEntries = null;
        }
        else
        {
            foreach (
                DataGridViewCell cell in
                dataGridViewHostsEntries.SelectedCells)
            {
                if (cell.ValueType == typeof(string))
                {
                    cell.Value = Clipboard.GetText();
                }
            }
        }
    }

    /// <summary>
    /// Occurs when form's Visible property changed.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnVisibleChanged(object sender, EventArgs e) => ShowInTaskbar = Visible;

    private void InitializeHotkeySupport()
    {
        ProfileSwitcher.ProfileError = msg =>
            notifyIcon.ShowBalloonTip(3000, "Profile Error", msg, ToolTipIcon.Error);

        HotkeyRegistry.HotkeyConflictNotify += (archive, meta) =>
        {
            var chord = $"{meta.HotkeyModifiers}+{meta.HotkeyKey}";
            notifyIcon.ShowBalloonTip(
                3000,
                "Hotkey Conflict",
                string.Format(Properties.Resources.ProfileHotkeyConflict, chord, archive.FileName),
                ToolTipIcon.Warning);
        };

        HotkeyRegistry.Initialize(Handle);

        // Build initial menus
        RebuildProfilesMenus();

        // Rebuild on list changes
        HostsArchiveList.Instance.ListChanged += (_, _) => RebuildProfilesMenus();
        ProfileSwitcher.ActiveArchiveChanged += RebuildProfilesMenus;
    }

    private void RebuildProfilesMenus()
    {
        if (InvokeRequired)
        {
            Invoke(RebuildProfilesMenus);
            return;
        }

        // Tray menu item
        if (_menuTrayProfiles == null)
        {
            _menuTrayProfiles = new ToolStripMenuItem("Profiles");
            int exitIndex = contextMenuTray.Items.IndexOf(contextMenuExit);
            contextMenuTray.Items.Insert(exitIndex, new ToolStripSeparator());
            contextMenuTray.Items.Insert(exitIndex, _menuTrayProfiles);
        }

        // Menu bar item — insert between View and Tools
        if (_menuBarProfiles == null)
        {
            _menuBarProfiles = new ToolStripMenuItem("Profiles");
            int toolsIndex = menuStrip.Items.IndexOf(menuTools);
            menuStrip.Items.Insert(toolsIndex, _menuBarProfiles);
        }

        PopulateProfilesMenu(_menuTrayProfiles);
        PopulateProfilesMenu(_menuBarProfiles);
    }

    private void PopulateProfilesMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var menuSaveCurrent = new ToolStripMenuItem("Save Current as Profile…");
        menuSaveCurrent.Click += OnSaveCurrentAsProfileClick;
        parent.DropDownItems.Add(menuSaveCurrent);

        var menuNewEmpty = new ToolStripMenuItem("New Empty Profile…");
        menuNewEmpty.Click += OnNewEmptyProfileClick;
        parent.DropDownItems.Add(menuNewEmpty);

        parent.DropDownItems.Add(new ToolStripSeparator());

        var menuDefault = new ToolStripMenuItem("Default")
        {
            Checked = ProfileSwitcher.ActiveArchive == null
        };
        menuDefault.Click += (_, _) => ProfileSwitcher.ClearActive();
        parent.DropDownItems.Add(menuDefault);

        var profiles = HostsArchiveList.Instance
            .OrderBy(a => a.Metadata?.SortOrder ?? 0)
            .ThenBy(a => a.FileName)
            .ToList();

        foreach (var archive in profiles)
        {
            var label = archive.FileName;
            var meta = archive.Metadata;
            if (meta?.HasHotkey == true)
                label += $"  ({FormatHotkeyChord(meta.HotkeyModifiers, meta.HotkeyKey)})";

            var profileItem = new ToolStripMenuItem(label)
            {
                Checked = ProfileSwitcher.ActiveArchive == archive
            };

            bool exists = File.Exists(archive.FilePath);
            var captured = archive;

            var menuActivate = new ToolStripMenuItem("Activate") { Enabled = exists };
            menuActivate.Click += (_, _) =>
                ProfileSwitcher.Activate(captured, ProfileSwitcher.TriggerSource.TrayMenu);

            var menuClone = new ToolStripMenuItem("Clone…") { Enabled = exists };
            menuClone.Click += (_, _) => OnCloneProfileClick(captured);

            var menuSettings = new ToolStripMenuItem("Profile Settings…");
            menuSettings.Click += (_, _) =>
            {
                using var dlg = new ProfileSettingsForm(captured);
                dlg.ShowDialog(this);
            };

            var menuDelete = new ToolStripMenuItem("Delete");
            menuDelete.Click += (_, _) => OnDeleteProfileClick(captured);

            profileItem.DropDownItems.Add(menuActivate);
            profileItem.DropDownItems.Add(new ToolStripSeparator());
            profileItem.DropDownItems.Add(menuClone);
            profileItem.DropDownItems.Add(menuSettings);
            profileItem.DropDownItems.Add(new ToolStripSeparator());
            profileItem.DropDownItems.Add(menuDelete);

            if (!exists)
                profileItem.ForeColor = SystemColors.GrayText;

            parent.DropDownItems.Add(profileItem);
        }
    }

    private void OnSaveCurrentAsProfileClick(object? sender, EventArgs e)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputArchivePrompt;

        if (inputDialog.ShowDialog(this) == DialogResult.OK)
        {
            HostsFile.Instance.Archive(inputDialog.Input);
        }
    }

    private void OnNewEmptyProfileClick(object? sender, EventArgs e)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputArchivePrompt;

        if (inputDialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = inputDialog.Input;
            var filePath = Path.Combine(HostsArchiveList.ArchiveDirectory, name);

            Directory.CreateDirectory(HostsArchiveList.ArchiveDirectory);

            // Write default Windows hosts content
            File.WriteAllText(filePath, HostsFileEditor.Properties.Resources.hosts);

            HostsArchiveList.Instance.Add(new HostsArchive { FilePath = filePath });
        }
    }

    private void OnCloneProfileClick(HostsArchive source)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputArchivePrompt;

        if (inputDialog.ShowDialog(this) == DialogResult.OK)
        {
            var destPath = Path.Combine(HostsArchiveList.ArchiveDirectory, inputDialog.Input);
            File.Copy(source.FilePath, destPath);
            HostsArchiveList.Instance.Add(new HostsArchive { FilePath = destPath });
        }
    }

    private void OnDeleteProfileClick(HostsArchive archive)
    {
        var result = MessageBox.Show(
            this,
            $"Delete profile '{archive.FileName}'?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            if (ProfileSwitcher.ActiveArchive == archive)
                ProfileSwitcher.ClearActive();

            HostsArchiveList.Instance.Delete(archive);
        }
    }

    private static string FormatHotkeyChord(int modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        parts.Add(((Keys)key).ToString());
        return string.Join("+", parts);
    }

    /// <summary>
    /// Updates the notify icon.
    /// </summary>
    private void UpdateNotifyIcon()
    {
        notifyIcon.Icon =
            HostsFile.IsEnabled ?
            Resources.HostsFileEditor :
            Resources.HostsFileEditorDisabled;
    }

    /// <summary>
    /// Called when insert above clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/>
    /// instance containing the event data.</param>
    private void OnInsertAboveClick(object sender, EventArgs e)
    {
        if (dataGridViewHostsEntries.CurrentRow?.DataBoundItem != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.InsertBefore(currentEntry);
            }
        }
        else
        {
            dataGridViewHostsEntries.CancelEdit();
            HostsFile.Instance.Entries.Add();
        }
    }

    /// <summary>
    /// Called when insert below clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnInsertBelowClick(object sender, EventArgs e)
    {
        if (dataGridViewHostsEntries.CurrentRow?.DataBoundItem != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.InsertAfter(currentEntry);
            }
        }
        else
        {
            dataGridViewHostsEntries.CancelEdit();
            HostsFile.Instance.Entries.Add();
        }
    }

    /// <summary>
    /// Called when refresh clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnRefreshClick(object sender, EventArgs e)
    {
        var result = MessageBox.Show(
            this,
            Resources.LoseChangesQuestion,
            Resources.LoseChangesDialogCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (result == DialogResult.Yes)
        {
            HostsFile.Instance.Refresh();
        }
    }

    /// <summary>
    /// Called when view archive clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnViewArchiveClick(object sender, EventArgs e)
    {
        bool isChecked = ((dynamic)sender).Checked;

        menuViewArchive.Checked = !isChecked;
        buttonViewArchive.Checked = !isChecked;

        splitContainer.Panel2Collapsed = isChecked;
    }

    /// <summary>
    /// Called when undo clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnUndoClick(object sender, EventArgs e) => UndoManager.Instance.Undo();

    /// <summary>
    /// Called when redo clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnRedoClick(object sender, EventArgs e) => UndoManager.Instance.Redo();

    /// <summary>
    /// Called when archive delete clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnArchiveDeleteClick(object sender, EventArgs e)
    {
        var archive = dataGridViewArchive.CurrentHostsArchive;

        if (archive != null)
        {
            HostsArchiveList.Instance.Delete(archive);
        }
    }

    /// <summary>
    /// Called when archive load clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance
    /// containing the event data.</param>
    private void OnArchiveLoadClick(object sender, EventArgs e)
    {
        var archive = dataGridViewArchive.CurrentHostsArchive;

        if (archive != null)
        {
            var beforeSnapshot = HostsFile.Instance.Entries.Select(ToSnapshot).ToList();
            var fromProfile = _currentProfileName;

            HostsFile.Instance.Import(archive.FilePath);

            var afterSnapshot = HostsFile.Instance.Entries.Select(ToSnapshot).ToList();
            var (added, removed, modified) = ComputeDiff(beforeSnapshot, afterSnapshot);
            _currentProfileName = archive.FileName;

            AuditLogger.Instance.Log(new AuditEntry
            {
                Action = nameof(AuditActionType.ProfileSwitch),
                Source = AuditSource.MainForm,
                Detail = new AuditDetail
                {
                    ProfileFrom = fromProfile,
                    ProfileTo = archive.FileName,
                    EntriesAdded = added.Count > 0 ? added : null,
                    EntriesRemoved = removed.Count > 0 ? removed : null,
                    EntriesModified = modified.Count > 0 ? modified : null
                }
            });
        }
    }

    /// <summary>
    /// Called when ping IPs clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance
    /// containing the event data.</param>
    private void OnPingIPsClick(object sender, EventArgs e)
    {
        bool isChecked = (sender as dynamic).Checked;

        HostsEntry.AutoPingIPAddress = !isChecked;

        menuPingIPs.Checked = !isChecked;
    }

    /// <summary>
    /// Called when remove default text clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> 
    /// instance containing the event data.</param>
    private void OnRemoveDefaultTextClick(object sender, EventArgs e)
    {
        menuRemoveDefaultText.Checked =
            !menuRemoveDefaultText.Checked;

        HostsFile.RemoveDefaultText =
            menuRemoveDefaultText.Checked;
    }

    /// <summary>
    /// Loads the settings.
    /// </summary>
    private void LoadSettings()
    {
        var settings = Settings.Default;

        HostsEntry.AutoPingIPAddress = settings.AutoPingIPAddresses;
        HostsFile.RemoveDefaultText = settings.RemoveDefaultText;

        menuPingIPs.Checked = HostsEntry.AutoPingIPAddress;
        menuRemoveDefaultText.Checked = HostsFile.RemoveDefaultText;
        menuViewArchive.Checked = settings.ArchiveVisible;
        buttonViewArchive.Checked = settings.ArchiveVisible;
        splitContainer.Panel2Collapsed = !settings.ArchiveVisible;

        // Do quick check that saved location still exists for multi-monitor setups
        if (Screen.AllScreens.Any(screen =>
            screen.WorkingArea.Contains(settings.WindowLocation)))
        {
            Location = settings.WindowLocation;
            Size = settings.WindowSize;
        }

        splitContainer.SplitterDistance = settings.SplitterWidth;
    }

    /// <summary>
    /// Saves the settings.
    /// </summary>
    private void SaveSettings()
    {
        var settings = Settings.Default;

        settings.AutoPingIPAddresses = HostsEntry.AutoPingIPAddress;
        settings.RemoveDefaultText = HostsFile.RemoveDefaultText;
        settings.WindowLocation = Location;
        settings.ArchiveVisible = menuViewArchive.Checked;

        // Save size for normal window, don't save anything for minimized
        // since that's probably not what the user wants next time
        // they open
        if (WindowState == FormWindowState.Normal)
        {
            settings.WindowSize = Size;
            settings.WindowState = WindowState;
        }
        else if (WindowState == FormWindowState.Maximized)
        {
            settings.WindowState = WindowState;
        }

        settings.SplitterWidth = splitContainer.SplitterDistance;

        settings.Save();
    }

    /// <summary>
    /// Called when resizing ends.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">
    /// The <see cref="System.EventArgs"/> instance containing the event 
    /// data.
    /// </param>
    private void OnResizingEnd(object sender, EventArgs e)
    {
        // Update settings to save last valid size 
        // (not in maximized or minimized mode)
        if (WindowState == FormWindowState.Normal)
        {
            Settings.Default.WindowSize = Size;
        }
    }

    /// <summary>
    /// Called when remove sort clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnRemoveSortClick(object sender, EventArgs e) => _hostEntriesView?.RemoveSort();

    /// <summary>
    /// Called when check clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnCheckClick(object sender, EventArgs e)
    {
        dataGridViewHostsEntries.CancelEdit();

        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            HostsFile.Instance.Entries.SetEnabled(
                 dataGridViewHostsEntries.SelectedHostEntries,
                 isEnabled: true);
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.SetEnabled(
                    [currentEntry],
                    isEnabled: true);
            }
        }
    }

    /// <summary>
    /// Called when uncheck clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnUncheckClick(object sender, EventArgs e)
    {
        dataGridViewHostsEntries.CancelEdit();

        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            HostsFile.Instance.Entries.SetEnabled(
                 dataGridViewHostsEntries.SelectedHostEntries,
                 isEnabled: false);
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            if (currentEntry != null)
            {
                HostsFile.Instance.Entries.SetEnabled(
                    [currentEntry],
                    isEnabled: false);
            }
        }
    }

    /// <summary>
    /// Called when about clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnAboutClick(object sender, EventArgs e) => (new AboutForm()).ShowDialog(this);

    /// <summary>
    /// Called when open text editor clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnOpenTextEditorClick(object sender, EventArgs e) => FileOpener.OpenTextFile(HostsFile.DefaultHostFilePath);
}
