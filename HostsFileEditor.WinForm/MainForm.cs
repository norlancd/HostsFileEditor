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

    /// <summary>Owns the diff-before-switch prompt — extracted to its own class (C1).</summary>
    private DiffBeforeSwitchGate? _diffGate;

    /// <summary>Owns watching for external edits to the hosts/profile files — extracted to its own class (C1).</summary>
    private ProfileExternalChangeWatcher? _externalChangeWatcher;

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
                AuditLogger.Instance.Log(AuditActionType.EntryRemoved, AuditSource.MainForm, new AuditDetail { Entry = snap });
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
        List<HostsEntry> entries;
        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            entries = dataGridViewHostsEntries.SelectedHostEntries.ToList();
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            // Clearing a single cell's text is never useful on its own — it just
            // leaves a half-blank row — so this always acts on the whole row instead.
            entries = [dataGridViewHostsEntries.CurrentHostEntry];
        }
        else
        {
            return;
        }

        var message = entries.Count == 1
            ? $"Delete this entry?\n\n{entries[0].UnparsedText}"
            : $"Delete these {entries.Count} entries?";

        var result = MessageBox.Show(this, message, Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        var snapshots = entries.Select(ToSnapshot).ToList();
        HostsFile.Instance.Entries.Remove(entries);
        foreach (var snap in snapshots)
        {
            AuditLogger.Instance.Log(AuditActionType.EntryRemoved, AuditSource.MainForm, new AuditDetail { Entry = snap });
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

        InitializeHotkeySupport();
        SetupAuditLoggerNotifications();
        AuditLogger.Instance.Initialize(); // after subscription so IntegrityFailed is handled
        TakeBaselineSnapshot();
        // RecoverFromRestart() must run before SetupRollbackTimer() — it's what
        // populates PendingStartupNotification, which SetupRollbackTimer() checks
        // immediately to show the "timer expired while closed" balloon. Calling it
        // after SetupRollbackTimer() (as before) meant that check always saw null.
        var startupRevertMessage = RollbackTimerService.Instance.RecoverFromRestart();
        if (startupRevertMessage != null)
            SyncActiveProfileAfterRevert();
        SetupRollbackTimer();

        _externalChangeWatcher = new ProfileExternalChangeWatcher(this, TakeBaselineSnapshot);
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
    private void OnImportClick(object sender, EventArgs e)
    {
        if (openFileDialog.ShowDialog(this) != DialogResult.OK) return;

        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;
        inputDialog.Input = GenerateUniqueProfileName(Path.GetFileNameWithoutExtension(openFileDialog.FileName));

        if (inputDialog.ShowDialog(this) != DialogResult.OK) return;

        dataGridViewHostsEntries.CommitEdit(DataGridViewDataErrorContexts.Commit);

        HostsFile.Instance.Import(openFileDialog.FileName);
        HostsFile.Instance.SaveAsProfile(inputDialog.Input);
        AuditLogger.Instance.Log(AuditActionType.FileImported, AuditSource.MainForm, new AuditDetail { SourcePath = openFileDialog.FileName });

        var imported = HostsProfileList.Instance.FirstOrDefault(p =>
            string.Equals(p.FileName, HostsProfile.NormalizeName(inputDialog.Input), StringComparison.OrdinalIgnoreCase));

        // Imported content is identical to what was just saved, so the diff-before-switch
        // dialog (if enabled) will skip itself and this activates silently — same as Clone.
        if (imported != null)
            ProfileSwitcher.Activate(imported, ProfileSwitcher.TriggerSource.TrayMenu);
    }

    /// <summary>
    /// Suggests a profile name for newly imported content: the source file's own
    /// name, unless that collides with an existing profile or a reserved name
    /// (matching what already shows up as a peer entry in the Profiles menu) —
    /// in which case " (2)", " (3)", etc. is appended until it's unique.
    /// </summary>
    private static string GenerateUniqueProfileName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "profile";

        // "default" isn't listed — it's now a real profile (Default.hosts), so
        // existingNames below already catches it like any other taken name.
        var reserved = new[] { "hosts", "disabled" };
        var existingNames = HostsProfileList.Instance
            .Select(p => Path.GetFileNameWithoutExtension(p.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsTaken(string name) =>
            reserved.Contains(name, StringComparer.OrdinalIgnoreCase) || existingNames.Contains(name);

        if (!IsTaken(baseName)) return baseName;

        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{baseName} ({counter})";
            counter++;
        } while (IsTaken(candidate));

        return candidate;
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
    /// Default is a real, persisted profile (reserved file name) like any other —
    /// always present, so it gets Raw Edit/Profile Settings/Activate Temporarily
    /// for free via the exact same code paths as user-created profiles.
    /// </summary>
    private static HostsProfile GetDefaultProfile() =>
        HostsProfileList.Instance.FirstOrDefault(p => p.IsDefault) ?? new HostsProfile("Default");

    /// <summary>
    /// "Default" in the Profiles menu — reached from the same submenu as
    /// Activate/Reload for named profiles, so it behaves like an actual switch.
    /// </summary>
    private void OnRestoreToDefaultProfileClick()
    {
        dataGridViewHostsEntries.CommitEdit(DataGridViewDataErrorContexts.Commit);

        if (!ProfileSwitcher.Activate(GetDefaultProfile(), ProfileSwitcher.TriggerSource.TrayMenu))
            return; // file missing, or user cancelled at the diff dialog

        AuditLogger.Instance.Log(AuditActionType.DefaultRestored, AuditSource.MainForm);
    }

    /// <summary>
    /// "Hosts File Disabled" — unlike "Default", which always has content
    /// (even if it's just the Windows default), this turns hosts resolution off
    /// entirely: no profile is active, not even Default.
    /// </summary>
    private void OnDisableAllProfilesClick()
    {
        dataGridViewHostsEntries.CommitEdit(DataGridViewDataErrorContexts.Commit);

        ProfileSwitcher.DisableAll();
        AuditLogger.Instance.Log(AuditActionType.HostsFileDisabled, AuditSource.MainForm);
    }

    /// <summary>
    /// The "Hosts File Disabled" checkbox in the File menu — checking it disables
    /// all profiles, unchecking it falls back to Default.
    /// </summary>
    private void OnHostsFileDisabledClick(object sender, EventArgs e)
    {
        if (ProfileSwitcher.IsHostsDisabled)
            OnRestoreToDefaultProfileClick();
        else
            OnDisableAllProfilesClick();
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
        var active = ProfileSwitcher.ActiveProfile;
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
    private void OnMoveDownClick(object sender, EventArgs e)
    {
        var entries = HostsFile.Instance.Entries;

        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            var selectedEntries = dataGridViewHostsEntries.SelectedHostEntries.ToList();
            var lastSelected = dataGridViewHostsEntries.LastSelectedHostEntry;

            // The block moves down by one position relative to whatever currently
            // sits right after it — NOT relative to one of its own members (using
            // the selection's own last item as the target was always a no-op).
            var lastIndex = lastSelected != null ? entries.IndexOf(lastSelected) : -1;
            if (lastIndex >= 0 && lastIndex < entries.Count - 1)
            {
                entries.MoveAfter(selectedEntries, entries[lastIndex + 1]);
                dataGridViewHostsEntries.SelectedHostEntries = selectedEntries;
            }
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            var currentIndex = entries.IndexOf(currentEntry);
            if (currentIndex >= 0 && currentIndex < entries.Count - 1)
            {
                entries.MoveAfter([currentEntry], entries[currentIndex + 1]);
                dataGridViewHostsEntries.SelectedHostEntries = [currentEntry];
            }
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
        var entries = HostsFile.Instance.Entries;

        if (dataGridViewHostsEntries.SelectedRows.Count > 0)
        {
            var selectedEntries = dataGridViewHostsEntries.SelectedHostEntries.ToList();
            var firstSelected = dataGridViewHostsEntries.FirstSelectedHostEntry;

            // The block moves up by one position relative to whatever currently
            // sits right before it — NOT relative to one of its own members (using
            // the selection's own last item as the target was always a no-op).
            var firstIndex = firstSelected != null ? entries.IndexOf(firstSelected) : -1;
            if (firstIndex > 0)
            {
                entries.MoveBefore(selectedEntries, entries[firstIndex - 1]);
                dataGridViewHostsEntries.SelectedHostEntries = selectedEntries;
            }
        }
        else if (dataGridViewHostsEntries.CurrentHostEntry != null)
        {
            var currentEntry = dataGridViewHostsEntries.CurrentHostEntry;
            var currentIndex = entries.IndexOf(currentEntry);
            if (currentIndex > 0)
            {
                entries.MoveBefore([currentEntry], entries[currentIndex - 1]);
                dataGridViewHostsEntries.SelectedHostEntries = [currentEntry];
            }
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

        HotkeyRegistry.HotkeyConflictNotify += (profile, meta) =>
        {
            var chord = $"{meta.HotkeyModifiers}+{meta.HotkeyKey}";
            notifyIcon.ShowBalloonTip(
                3000,
                "Hotkey Conflict",
                string.Format(Properties.Resources.ProfileHotkeyConflict, chord, profile.FileName),
                ToolTipIcon.Warning);
        };

        HotkeyRegistry.Initialize(Handle);

        // OnVisibleChanged toggles ShowInTaskbar (hide-to-tray / restore), and changing
        // that property on a Form whose handle already exists forces WinForms to destroy
        // and recreate the native window handle. HotkeyRegistry's stored hwnd would then
        // point at a dead window, making every RegisterHotKey/UnregisterHotKey call fail
        // with ERROR_INVALID_WINDOW_HANDLE — re-sync on every recreation to stay valid.
        HandleCreated += (_, _) =>
        {
            HotkeyRegistry.Initialize(Handle);

            // $this.Icon is only applied once, from InitializeComponent(), before the
            // first handle ever exists — a later recreation leaves the new handle with
            // no icon set, so Windows falls back to the generic .NET host icon in the
            // taskbar. Re-apply it explicitly every time the handle is (re)created.
            Icon = Properties.Resources.HostsFileEditor;
        };

        _diffGate = new DiffBeforeSwitchGate(this, Text);
        menuDiffBeforeSwitch.Checked = Properties.Settings.Default.DiffBeforeSwitchEnabled;

        RestoreActiveProfile();

        // Build initial menus and tray icon
        RebuildProfilesMenus();
        UpdateNotifyIcon();
        UpdateGridEnabledState();

        // Rebuild on list changes
        HostsProfileList.Instance.ListChanged += (_, _) => RebuildProfilesMenus();
        ProfileSwitcher.ActiveProfileChanged += RebuildProfilesMenus;
        ProfileSwitcher.ActiveProfileChanged += UpdateNotifyIcon;
        ProfileSwitcher.ActiveProfileChanged += UpdateGridEnabledState;

        // Subscribed after RestoreActiveProfile() runs, so resuming the previous
        // session's active profile on launch doesn't itself pop a notification —
        // only real switches made while the app is running do.
        ProfileSwitcher.ActiveProfileChanged += NotifyProfileSwitched;
    }

    private void NotifyProfileSwitched()
    {
        var active = ProfileSwitcher.ActiveProfile;
        var message = ProfileSwitcher.IsHostsDisabled
            ? "Hosts file disabled — no profile active"
            : active is { IsDefault: false }
                ? $"Switched to profile \"{active.FileName}\""
                : "Switched to Default";
        notifyIcon.ShowBalloonTip(3000, "Profiles", message, ToolTipIcon.Info);
    }

    /// <summary>
    /// Restores tracking of which profile was active in a previous session.
    /// Falls back to comparing file contents if the saved name no longer matches
    /// (e.g. profile renamed/deleted, or hosts file edited outside this app).
    /// </summary>
    private void RestoreActiveProfile()
    {
        if (!HostsFile.IsEnabled)
        {
            ProfileSwitcher.RestoreDisabled();
            return;
        }

        var savedName = Properties.Settings.Default.ActiveProfileName;

        var match = !string.IsNullOrEmpty(savedName)
            ? HostsProfileList.Instance.FirstOrDefault(a => a.FileName == savedName)
            : null;

        match ??= DetectActiveProfileByContent();

        if (match != null)
        {
            ProfileSwitcher.RestoreActive(match);
        }
    }

    /// <summary>
    /// Re-syncs which profile is tracked as "active" after a rollback-timer revert,
    /// since the revert writes the hosts file directly (bypassing ProfileSwitcher) —
    /// without this, the Profiles menu checkmark and status bar would keep pointing
    /// at the timed profile that was just reverted away from.
    /// </summary>
    private static void SyncActiveProfileAfterRevert()
    {
        var match = DetectActiveProfileByContent();
        if (match != null)
            ProfileSwitcher.RestoreActive(match);
        else
            ProfileSwitcher.ClearActive();
    }

    private static HostsProfile? DetectActiveProfileByContent()
    {
        try
        {
            var hostsLines = File.ReadAllLines(HostsFile.DefaultHostFilePath);

            foreach (var profile in HostsProfileList.Instance)
            {
                if (File.Exists(profile.FilePath) &&
                    File.ReadAllLines(profile.FilePath).SequenceEqual(hostsLines))
                {
                    return profile;
                }
            }
        }
        catch (IOException)
        {
            // Hosts file unreadable — leave active profile undetermined
        }

        return null;
    }

    private void RebuildProfilesMenus()
    {
        if (InvokeRequired)
        {
            Invoke(RebuildProfilesMenus);
            return;
        }

        PopulateProfilesMenu(menuTrayProfiles);
        PopulateProfilesMenu(menuBarProfiles);
    }

    private void PopulateProfilesMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var menuSaveCurrent = new ToolStripMenuItem("Save Current as Profile…") { Enabled = !ProfileSwitcher.IsHostsDisabled };
        menuSaveCurrent.Click += OnSaveCurrentAsProfileClick;
        parent.DropDownItems.Add(menuSaveCurrent);

        var menuNewEmpty = new ToolStripMenuItem("New Empty Profile…") { Enabled = !ProfileSwitcher.IsHostsDisabled };
        menuNewEmpty.Click += OnNewEmptyProfileClick;
        parent.DropDownItems.Add(menuNewEmpty);

        parent.DropDownItems.Add(new ToolStripSeparator());

        var defaultProfile = GetDefaultProfile();
        bool isDefaultActive = ProfileSwitcher.ActiveProfile == defaultProfile && !ProfileSwitcher.IsHostsDisabled;
        var defaultFontStyle = isDefaultActive ? (FontStyle.Italic | FontStyle.Bold) : FontStyle.Italic;
        var menuDefault = BuildProfileMenuItem(parent, defaultProfile, "Default", new Font(parent.Font, defaultFontStyle),
            isDefaultActive, includeDelete: false);
        parent.DropDownItems.Add(menuDefault);

        parent.DropDownItems.Add(new ToolStripSeparator());

        // ── User-created profiles ──────────────────────────────────────────
        var profiles = HostsProfileList.Instance
            .Where(a => !a.IsDefault)
            .OrderBy(a => a.Metadata?.SortOrder ?? 0)
            .ThenBy(a => a.FileName)
            .ToList();

        foreach (var profile in profiles)
        {
            bool isActive = ProfileSwitcher.ActiveProfile == profile && !ProfileSwitcher.IsHostsDisabled;
            var font = isActive ? new Font(parent.Font, FontStyle.Bold) : parent.Font;
            var profileItem = BuildProfileMenuItem(parent, profile, profile.FileName, font, isActive, includeDelete: true);
            parent.DropDownItems.Add(profileItem);
        }

        parent.DropDownItems.Add(new ToolStripSeparator());

        // A toggle, not a peer "switch to this" entry like Default/profiles —
        // checking it disables hosts resolution entirely, so it sits below the list
        // rather than next to Default, where it could be mistaken for one more profile.
        var menuHostsDisabled = new ToolStripMenuItem("Hosts File Disabled")
        {
            Checked = ProfileSwitcher.IsHostsDisabled,
            CheckOnClick = true
        };
        menuHostsDisabled.Click += (s, e) => OnHostsFileDisabledClick(s!, e);
        parent.DropDownItems.Add(menuHostsDisabled);
    }

    /// <summary>
    /// Builds one top-level Profiles-menu entry (Default or a named profile) with its
    /// Activate/Clone/Raw Edit/Settings[/Delete] submenu. Shared so Default gets exactly
    /// the same capabilities as user-created profiles, minus the ability to delete it.
    /// </summary>
    private ToolStripMenuItem BuildProfileMenuItem(
        ToolStripMenuItem parent, HostsProfile profile, string baseLabel, Font font, bool isActive, bool includeDelete)
    {
        var meta = profile.Metadata;
        var label = baseLabel;
        if (meta?.HasHotkey == true)
            label += $"  ({FormatHotkeyChord(meta.HotkeyModifiers, meta.HotkeyKey)})";

        var item = new ToolStripMenuItem(label)
        {
            Checked = isActive,
            Enabled = !ProfileSwitcher.IsHostsDisabled,
            Font = font
        };

        bool exists = File.Exists(profile.FilePath);
        var captured = profile;

        // Already active: re-activating just discards unsaved grid edits and
        // reloads from this profile's file — relabel so that's clear, rather
        // than reading as a redundant "activate the thing that's already active".
        var menuActivate = new ToolStripMenuItem(isActive ? "Reload" : "Activate") { Enabled = exists, Font = parent.Font };
        menuActivate.Click += (_, _) =>
            ProfileSwitcher.Activate(captured, ProfileSwitcher.TriggerSource.TrayMenu);

        var menuClone = new ToolStripMenuItem("Clone…") { Enabled = exists, Font = parent.Font };
        menuClone.Click += (_, _) => OnCloneProfileClick(captured);

        var menuRawEditProfile = new ToolStripMenuItem("Raw Edit…") { Enabled = exists, Font = parent.Font };
        menuRawEditProfile.Click += (_, _) => OnProfileRawEditClick(captured);

        var menuSettings = new ToolStripMenuItem("Profile Settings…") { Font = parent.Font };
        menuSettings.Click += (_, _) =>
        {
            using var dlg = new ProfileSettingsForm(captured);
            dlg.ShowDialog(this);
            RebuildProfilesMenus(); // color/sort-order/description changes don't fire ListChanged
        };

        // Doesn't make sense for the profile that's already active — there's
        // nothing to "temporarily switch to", you're already on it.
        var menuActivateTimer = new ToolStripMenuItem("Activate Temporarily…")
        {
            Enabled = exists,
            Visible = !isActive,
            Font = parent.Font
        };
        menuActivateTimer.Click += (_, _) => OnActivateWithTimerClick(captured);

        item.DropDownItems.Add(menuActivate);
        item.DropDownItems.Add(menuActivateTimer);
        item.DropDownItems.Add(new ToolStripSeparator());
        item.DropDownItems.Add(menuClone);
        item.DropDownItems.Add(menuRawEditProfile);
        item.DropDownItems.Add(menuSettings);

        if (includeDelete)
        {
            var menuDelete = new ToolStripMenuItem("Delete") { Font = parent.Font };
            menuDelete.Click += (_, _) => OnDeleteProfileClick(captured);
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(menuDelete);
        }

        if (!exists)
            item.ForeColor = SystemColors.GrayText;

        var colorHex = meta?.Color;
        if (!string.IsNullOrEmpty(colorHex))
        {
            try
            {
                item.Image = CreateColorSwatch(ColorTranslator.FromHtml(colorHex), 12, 12);
                item.ImageScaling = ToolStripItemImageScaling.None;
            }
            catch { }
        }

        return item;
    }

    private void OnSaveCurrentAsProfileClick(object? sender, EventArgs e)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;

        if (inputDialog.ShowDialog(this) == DialogResult.OK)
        {
            // Flush any in-progress grid edit so the saved profile matches what's on screen
            dataGridViewHostsEntries.CommitEdit(DataGridViewDataErrorContexts.Commit);

            HostsFile.Instance.SaveAsProfile(inputDialog.Input);
        }
    }

    private void OnNewEmptyProfileClick(object? sender, EventArgs e)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;

        if (inputDialog.ShowDialog(this) == DialogResult.OK)
        {
            var profile = new HostsProfile(inputDialog.Input);

            Directory.CreateDirectory(HostsProfileList.ProfileDirectory);

            // Write default Windows hosts content
            File.WriteAllText(profile.FilePath, Properties.Resources.hosts);

            HostsProfileList.Instance.Add(profile);
        }
    }

    private void OnCloneProfileClick(HostsProfile source)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;

        if (inputDialog.ShowDialog(this) == DialogResult.OK)
        {
            // If cloning the profile currently loaded on screen, sync its saved file
            // with the live (possibly edited, uncommitted) grid contents first
            if (source == ProfileSwitcher.ActiveProfile)
            {
                dataGridViewHostsEntries.CommitEdit(DataGridViewDataErrorContexts.Commit);
                HostsFile.Instance.SaveAs(source.FilePath);
                _externalChangeWatcher?.NotifyActiveProfileFileWritten();
            }

            var destProfile = new HostsProfile(inputDialog.Input);
            File.Copy(source.FilePath, destProfile.FilePath);
            HostsProfileList.Instance.Add(destProfile);

            // Clone is identical to the source at this point, so the diff-before-switch
            // dialog (if enabled) will skip itself and this activates silently
            ProfileSwitcher.Activate(destProfile, ProfileSwitcher.TriggerSource.TrayMenu);
        }
    }

    private void OnDeleteProfileClick(HostsProfile profile)
    {
        bool isActive = ProfileSwitcher.ActiveProfile == profile;

        var prompt = isActive
            ? $"Delete profile '{profile.FileName}'?\n\nIt's currently active — the hosts file will be reset to the Windows default."
            : $"Delete profile '{profile.FileName}'?";

        var result = MessageBox.Show(
            this,
            prompt,
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            // Deleting the file out from under the active profile would otherwise leave
            // the grid/live hosts file showing an orphaned copy of content that no
            // longer corresponds to anything — reset to a well-defined state instead.
            if (isActive)
                OnRestoreToDefaultProfileClick();

            HostsProfileList.Instance.Delete(profile);
        }
    }

    private void UpdateNotifyIcon()
    {
        notifyIcon.Icon = ProfileSwitcher.IsHostsDisabled
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
        bool enabled = !ProfileSwitcher.IsHostsDisabled;

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

    private void OnRawEditClick(object? sender, EventArgs e)
    {
        var rawText = RawEditForm.FormatAligned(HostsFile.Instance.Entries);

        using var form = new RawEditForm(rawText) { Icon = Icon };
        if (form.ShowDialog(this) != DialogResult.OK) return;

        HostsFile.Instance.ImportFromLines(form.GetLines());
        TakeBaselineSnapshot();
    }

    private void OnProfileRawEditClick(HostsProfile profile)
    {
        if (!File.Exists(profile.FilePath))
        {
            MessageBox.Show(this, $"Profile file not found:\n{profile.FilePath}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        HostsEntryList entries;
        try
        {
            entries = new HostsEntryList(File.ReadAllLines(profile.FilePath), filterDefault: false);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Could not read profile:\n{ex.Message}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var rawText = RawEditForm.FormatAligned(entries);

        using var form = new RawEditForm(rawText) { Icon = Icon, Text = $"Raw Edit — {profile.FileName}" };
        if (form.ShowDialog(this) != DialogResult.OK) return;

        // Writes only the profile's own file — never the live hosts file or grid.
        // If this happens to be the active profile, the existing external-change
        // watcher will naturally offer to reload it into the grid on next focus.
        File.WriteAllLines(profile.FilePath, form.GetLines());
    }

    internal static Bitmap CreateColorSwatch(Color c, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(new SolidBrush(c), 0, 0, w, h);
        g.DrawRectangle(new Pen(Color.FromArgb(100, 0, 0, 0)), 0, 0, w - 1, h - 1);
        return bmp;
    }
}
