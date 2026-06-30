using Equin.ApplicationFramework;
using HostsFileEditor.Extensions;
using HostsFileEditor.Properties;
using HostsFileEditor.Utilities;

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
    /// Determines if user is currently adding a new row.  Used for ugly
    /// hacks setup in load event.
    /// </summary>
    private bool _addingNew;

    /// <summary>
    /// Ignore adding new in progress. Used for ugly hacks setup in load
    /// event.
    /// </summary>
    private bool _ignoreAddingNew;

    /// <summary>Owns the diff-before-switch prompt — extracted to its own class (C1).</summary>
    private DiffBeforeSwitchGate? _diffGate;

    /// <summary>Owns watching for external edits to the hosts/profile files — extracted to its own class (C1).</summary>
    private ProfileExternalChangeWatcher? _externalChangeWatcher;

    /// <summary>Owns the Profiles menu and all profile CRUD actions — extracted to its own class (C1).</summary>
    private ProfilesMenuController? _profilesMenu;

    /// <summary>Owns the grid's row-editing commands (Copy/Cut/Paste/Delete/Duplicate/Move/Insert/Check/Uncheck) — extracted to its own class (C1).</summary>
    private GridEditCommands? _gridCommands;

    /// <summary>Injected rather than read from <see cref="AuditLogger.Instance"/> directly — part of the DIP cleanup.</summary>
    private readonly IAuditLogger _auditLogger;

    /// <summary>Injected rather than read from <see cref="RollbackTimerService.Instance"/> directly — part of the DIP cleanup.</summary>
    private readonly IRollbackTimerService _rollbackTimerService;

    /// <summary>Injected rather than read from <see cref="Utilities.UndoManager.Instance"/> directly — part of the DIP cleanup.</summary>
    private readonly IUndoManager _undoManager;

    /// <summary>Injected rather than read from <see cref="HostsProfileList.Instance"/> directly — part of the DIP cleanup.</summary>
    private readonly IHostsProfileList _profileList;

    /// <summary>Constructed once in Program.cs (shared with WinUI via Core) and injected here.</summary>
    private readonly IProfileSwitcher _profileSwitcher;

    /// <summary>Injected rather than read from <see cref="HotkeyRegistry.Instance"/> directly — part of the DIP cleanup.</summary>
    private readonly IHotkeyRegistry _hotkeyRegistry;

    private readonly IProfileExportImportService _exportImportService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    public MainForm(IAuditLogger auditLogger, IRollbackTimerService rollbackTimerService, IUndoManager undoManager, IHostsProfileList profileList, IProfileSwitcher profileSwitcher, IHotkeyRegistry hotkeyRegistry, IProfileExportImportService exportImportService)
    {
        _auditLogger = auditLogger;
        _rollbackTimerService = rollbackTimerService;
        _undoManager = undoManager;
        _profileList = profileList;
        _profileSwitcher = profileSwitcher;
        _hotkeyRegistry = hotkeyRegistry;
        _exportImportService = exportImportService;

        InitializeComponent();

        saveFileDialog.InitialDirectory = HostsFile.DefaultHostFilePath;

        // Prevent data binding from setting properties to null when
        // an empty string is typed in
        columnComment.DefaultCellStyle.NullValue = null;
        columnIpAddress.DefaultCellStyle.NullValue = null;
        columnHostnames.DefaultCellStyle.NullValue = null;

        _gridCommands = new GridEditCommands(
            this, dataGridViewHostsEntries, _auditLogger,
            menuCopy, menuContextCopy,
            menuCut, menuContextCut,
            menuPaste, menuContextPaste);
    }

    private const int WmHotkey = 0x0312;

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
            var profile = _hotkeyRegistry.GetProfileById(id);
            if (profile != null)
                _ = _profileSwitcher.ActivateAsync(profile, ProfileSwitcher.TriggerSource.TrayHotkey);
        }

        base.WndProc(ref message);
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
    private void OnCopyClick(object sender, EventArgs e) => _gridCommands!.Copy(sender, e);

    /// <summary>
    /// Occurs when cut clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnCutClick(object sender, EventArgs e) => _gridCommands!.Cut(sender, e);

    /// <summary>
    /// Occurs when delete clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnDeleteClick(object sender, EventArgs e) => _gridCommands!.Delete(sender, e);

    /// <summary>
    /// Occurs when duplicate clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnDuplicateClick(object sender, EventArgs e) => _gridCommands!.Duplicate(sender, e);

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

        bindingSourceHostFile.DataSource = HostsFile.Instance;

        _hostEntriesView = new BindingListView<HostsEntry>(components)
        {
            DataSource = HostsFile.Instance.Entries
        };

        _hostEntriesView.AddingNew += (s, args) => args.NewObject = new HostsEntry(HostsFile.Instance.Entries.UndoManager, string.Empty);

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

        InitializeHotkeySupport();
        SetupAuditLoggerNotifications();
        _auditLogger.Initialize(); // after subscription so IntegrityFailed is handled
        TakeBaselineSnapshot();
        // RecoverFromRestart() must run before SetupRollbackTimer() — it's what
        // populates PendingStartupNotification, which SetupRollbackTimer() checks
        // immediately to show the "timer expired while closed" balloon. Calling it
        // after SetupRollbackTimer() (as before) meant that check always saw null.
        var startupRevertMessage = _rollbackTimerService.RecoverFromRestart();
        if (startupRevertMessage != null)
            _profileSwitcher.SyncActiveAfterExternalWrite();
        SetupRollbackTimer();

        _externalChangeWatcher = new ProfileExternalChangeWatcher(this, TakeBaselineSnapshot, _profileSwitcher);
        FormClosed += (_, _) => _externalChangeWatcher?.Dispose();

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
    private async void OnImportClick(object sender, EventArgs e)
    {
        if (openFileDialog.ShowDialog(this) != DialogResult.OK) return;

        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;
        inputDialog.Input = _profilesMenu!.GenerateUniqueProfileName(Path.GetFileNameWithoutExtension(openFileDialog.FileName));

        if (inputDialog.ShowDialog(this) != DialogResult.OK) return;

        dataGridViewHostsEntries.CommitEdit(DataGridViewDataErrorContexts.Commit);

        HostsFile.Instance.Import(openFileDialog.FileName);
        _auditLogger.Log(AuditActionType.FileImported, AuditSource.MainForm, new AuditDetail { SourcePath = openFileDialog.FileName });

        await _profilesMenu!.ActivateAsNewProfile(inputDialog.Input);
    }

    private void OnFileExportProfilesClick(object? sender, EventArgs e)
        => _profilesMenu?.ShowExportProfilesDialog();

    private void OnFileImportProfilesClick(object? sender, EventArgs e)
        => _profilesMenu?.ShowImportProfilesDialog();

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

    private void OnDiffBeforeSwitchCheckedChanged(object sender, EventArgs e)
    {
        Properties.Settings.Default.DiffBeforeSwitchEnabled = menuDiffBeforeSwitch.Checked;
        Properties.Settings.Default.Save();
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

        if (HostsFile.Instance.WasModifiedExternally())
        {
            var result = MessageBox.Show(
                this,
                "The hosts file was modified outside this application since it was last loaded.\n\nOverwrite it with what's on screen?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes) return;
        }

        LogSaveChanges();
        HostsFile.Instance.Save();

        // Keep the active profile's own file in sync with what was just saved,
        // so edits made after activating a profile aren't lost on next activation
        var active = _profileSwitcher.ActiveProfile;
        if (active != null && File.Exists(active.FilePath))
        {
            HostsFile.Instance.SaveAs(active.FilePath);
            _externalChangeWatcher?.NotifyActiveProfileFileWritten();
        }
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
    private void OnMoveDownClick(object sender, EventArgs e) => _gridCommands!.MoveDown(sender, e);

    /// <summary>
    /// Occurs when move up clicked.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private void OnMoveUpClick(object sender, EventArgs e) => _gridCommands!.MoveUp(sender, e);

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
    private void OnPasteClick(object sender, EventArgs e) => _gridCommands!.Paste(sender, e);

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
        // A balloon tip is too easy to miss (and Windows may suppress it entirely
        // depending on notification settings) — a real dialog can't be missed.
        _profileSwitcher.ProfileError = msg =>
            MessageBox.Show(this, msg, "Profile Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        _hotkeyRegistry.HotkeyConflictNotify += (profile, meta) =>
        {
            var chord = $"{meta.HotkeyModifiers}+{meta.HotkeyKey}";
            notifyIcon.ShowBalloonTip(
                3000,
                "Hotkey Conflict",
                string.Format(Properties.Resources.ProfileHotkeyConflict, chord, profile.FileName),
                ToolTipIcon.Warning);
        };

        _hotkeyRegistry.Initialize(Handle);

        // OnVisibleChanged toggles ShowInTaskbar (hide-to-tray / restore), and changing
        // that property on a Form whose handle already exists forces WinForms to destroy
        // and recreate the native window handle. HotkeyRegistry's stored hwnd would then
        // point at a dead window, making every RegisterHotKey/UnregisterHotKey call fail
        // with ERROR_INVALID_WINDOW_HANDLE — re-sync on every recreation to stay valid.
        HandleCreated += (_, _) =>
        {
            _hotkeyRegistry.Initialize(Handle);

            // $this.Icon is only applied once, from InitializeComponent(), before the
            // first handle ever exists — a later recreation leaves the new handle with
            // no icon set, so Windows falls back to the generic .NET host icon in the
            // taskbar. Re-apply it explicitly every time the handle is (re)created.
            Icon = Properties.Resources.HostsFileEditor;
        };

        _diffGate = new DiffBeforeSwitchGate(this, Text, _profileSwitcher);
        menuDiffBeforeSwitch.Checked = Properties.Settings.Default.DiffBeforeSwitchEnabled;

        // Subscribes itself to HostsProfileList.Instance.ListChanged and
        // _profileSwitcher.ActiveProfileChanged, so it keeps the Profiles menu in
        // sync on its own from here on — constructed before RestoreFromSettings()
        // so that call's own ActiveProfileChanged also triggers an initial build.
        _profilesMenu = new ProfilesMenuController(
            this, dataGridViewHostsEntries, _auditLogger, _profileList, _profileSwitcher, _hotkeyRegistry, _exportImportService, menuBarProfiles, menuTrayProfiles,
            () => _externalChangeWatcher?.NotifyActiveProfileFileWritten(),
            OnActivateWithTimerClick);

        _profileSwitcher.RestoreFromSettings();

        // Build initial menus and tray icon (RestoreFromSettings() above already
        // triggers this when it finds a match, but not when the active profile is
        // left undetermined — so build explicitly too, redundant but harmless)
        _profilesMenu.Rebuild();
        UpdateNotifyIcon();
        UpdateGridEnabledState();

        _profileSwitcher.ActiveProfileChanged += UpdateNotifyIcon;
        _profileSwitcher.ActiveProfileChanged += UpdateGridEnabledState;

        // Subscribed after RestoreFromSettings() runs, so resuming the previous
        // session's active profile on launch doesn't itself pop a notification —
        // only real switches made while the app is running do.
        _profileSwitcher.ActiveProfileChanged += NotifyProfileSwitched;
    }

    private void NotifyProfileSwitched()
    {
        var active = _profileSwitcher.ActiveProfile;
        var message = _profileSwitcher.IsHostsDisabled
            ? "Hosts file disabled — no profile active"
            : active is { IsDefault: false }
                ? $"Switched to profile \"{active.FileName}\""
                : "Switched to Default";
        notifyIcon.ShowBalloonTip(3000, "Profiles", message, ToolTipIcon.Info);
    }

    private void UpdateNotifyIcon()
    {
        notifyIcon.Icon = _profileSwitcher.IsHostsDisabled
            ? Resources.HostsFileEditorDisabled
            : Resources.HostsFileEditor;
    }

    /// <summary>
    /// While hosts resolution is off there's no live content to act on, so the grid
    /// and everything that operates on it or brings in new content (Edit menu, the
    /// toolbar's table actions, Save/Save As/Import/Open in Text Editor/Raw Edit,
    /// Profiles/Default) go inactive. The "Hosts File Disabled" toggle itself is the
    /// only thing left enabled — it's the one way back out.
    /// </summary>
    private void UpdateGridEnabledState()
    {
        bool enabled = !_profileSwitcher.IsHostsDisabled;

        dataGridViewHostsEntries.Enabled = enabled;

        // Disable the children, not editToolStripMenuItem itself — disabling the
        // parent would stop the dropdown from opening at all, so the items inside
        // would no longer be "shown but disabled," they'd just be unreachable.
        foreach (ToolStripItem item in editToolStripMenuItem.DropDownItems)
            item.Enabled = enabled;

        foreach (ToolStripItem item in toolStrip.Items)
            item.Enabled = enabled;

        menuSave.Enabled = enabled;
        menuSaveAs.Enabled = enabled;
        menuImport.Enabled = enabled;
        openTextEditor.Enabled = enabled;
        menuRawEdit.Enabled = enabled;

        menuFilterComments.Enabled = enabled;
        menuFilterDisabled.Enabled = enabled;
        menuRemoveSort.Enabled = enabled;
    }


    /// <summary>
    /// Called when insert above clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/>
    /// instance containing the event data.</param>
    private void OnInsertAboveClick(object sender, EventArgs e) => _gridCommands!.InsertAbove(sender, e);

    /// <summary>
    /// Called when insert below clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnInsertBelowClick(object sender, EventArgs e) => _gridCommands!.InsertBelow(sender, e);

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
    /// Called when undo clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnUndoClick(object sender, EventArgs e) => _undoManager.Undo();

    /// <summary>
    /// Called when redo clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance 
    /// containing the event data.</param>
    private void OnRedoClick(object sender, EventArgs e) => _undoManager.Redo();

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

        // Do quick check that saved location still exists for multi-monitor setups
        if (Screen.AllScreens.Any(screen =>
            screen.WorkingArea.Contains(settings.WindowLocation)))
        {
            Location = settings.WindowLocation;
            Size = settings.WindowSize;
        }
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
    private void OnCheckClick(object sender, EventArgs e) => _gridCommands!.Check(sender, e);

    /// <summary>
    /// Called when uncheck clicked.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void OnUncheckClick(object sender, EventArgs e) => _gridCommands!.Uncheck(sender, e);

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

    private void OnRawEditClick(object? sender, EventArgs e)
    {
        var rawText = RawEditForm.FormatAligned(HostsFile.Instance.Entries);

        using var form = new RawEditForm(rawText) { Icon = Icon };
        if (form.ShowDialog(this) != DialogResult.OK) return;

        HostsFile.Instance.ImportFromLines(form.GetLines());
        TakeBaselineSnapshot();
    }

}
