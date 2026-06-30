using HostsFileEditor.Controls;

namespace HostsFileEditor;

/// <summary>
/// Owns the Profiles menu (tray + menu bar): building/rebuilding its contents and
/// every profile-CRUD action reached from it (Save Current/New Empty/Clone/Raw
/// Edit/Settings/Delete, Default, Hosts File Disabled, and import-as-profile).
/// Extracted out of MainForm so this concern can be reasoned about independently
/// of grid-editing/audit logic living in the rest of the form (C1).
/// </summary>
internal sealed class ProfilesMenuController
{
    private readonly Form _owner;
    private readonly HostsEntryDataGridView _grid;
    private readonly IAuditLogger _auditLogger;
    private readonly IHostsProfileList _profileList;
    private readonly IProfileSwitcher _profileSwitcher;
    private readonly IHotkeyRegistry _hotkeyRegistry;
    private readonly IProfileExportImportService _exportImportService;
    private readonly ToolStripMenuItem _menuBar;
    private readonly ToolStripMenuItem _menuTray;
    private readonly Action _notifyActiveProfileFileWritten;
    private readonly Action<HostsProfile> _activateWithTimer;

    public ProfilesMenuController(
        Form owner,
        HostsEntryDataGridView grid,
        IAuditLogger auditLogger,
        IHostsProfileList profileList,
        IProfileSwitcher profileSwitcher,
        IHotkeyRegistry hotkeyRegistry,
        IProfileExportImportService exportImportService,
        ToolStripMenuItem menuBar,
        ToolStripMenuItem menuTray,
        Action notifyActiveProfileFileWritten,
        Action<HostsProfile> activateWithTimer)
    {
        _owner = owner;
        _grid = grid;
        _auditLogger = auditLogger;
        _profileList = profileList;
        _profileSwitcher = profileSwitcher;
        _hotkeyRegistry = hotkeyRegistry;
        _exportImportService = exportImportService;
        _menuBar = menuBar;
        _menuTray = menuTray;
        _notifyActiveProfileFileWritten = notifyActiveProfileFileWritten;
        _activateWithTimer = activateWithTimer;

        _profileList.ListChanged += (_, _) => Rebuild();
        _profileSwitcher.ActiveProfileChanged += Rebuild;
    }

    public void Rebuild()
    {
        if (_owner.InvokeRequired)
        {
            _owner.Invoke(Rebuild);
            return;
        }

        PopulateProfilesMenu(_menuTray);
        PopulateProfilesMenu(_menuBar);
    }

    /// <summary>
    /// Saves content already loaded into <see cref="HostsFile.Instance"/>'s Entries
    /// as a new profile and activates it. Used by File &gt; Import once the caller
    /// has read the source file in and the user has picked a name for it.
    /// </summary>
    public async Task ActivateAsNewProfile(string profileName)
    {
        HostsFile.Instance.SaveAsProfile(profileName);

        var imported = _profileList.FirstOrDefault(p =>
            string.Equals(p.FileName, HostsProfile.NormalizeName(profileName), StringComparison.OrdinalIgnoreCase));

        // Imported content is identical to what was just saved, so the diff-before-switch
        // dialog (if enabled) will skip itself and this activates silently — same as Clone.
        if (imported != null)
            await _profileSwitcher.ActivateAsync(imported, ProfileSwitcher.TriggerSource.TrayMenu);
    }

    /// <summary>
    /// Suggests a profile name for newly imported content: the source file's own
    /// name, unless that collides with an existing profile or a reserved name
    /// (matching what already shows up as a peer entry in the Profiles menu) —
    /// in which case " (2)", " (3)", etc. is appended until it's unique.
    /// </summary>
    public string GenerateUniqueProfileName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "profile";

        // "default" isn't listed — it's now a real profile (Default.hosts), so
        // existingNames below already catches it like any other taken name.
        var reserved = new[] { "hosts", "disabled" };
        var existingNames = _profileList
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
    /// Default is a real, persisted profile (reserved file name) like any other —
    /// always present, so it gets Raw Edit/Profile Settings/Activate Temporarily
    /// for free via the exact same code paths as user-created profiles.
    /// </summary>
    private HostsProfile GetDefaultProfile() =>
        _profileList.FirstOrDefault(p => p.IsDefault) ?? new HostsProfile("Default");

    /// <summary>
    /// "Default" in the Profiles menu — reached from the same submenu as
    /// Activate/Reload for named profiles, so it behaves like an actual switch.
    /// </summary>
    public async Task RestoreToDefault()
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        if (!await _profileSwitcher.ActivateAsync(GetDefaultProfile(), ProfileSwitcher.TriggerSource.TrayMenu))
            return; // file missing, or user cancelled at the diff dialog

        _auditLogger.Log(AuditActionType.DefaultRestored, AuditSource.MainForm);
    }

    /// <summary>
    /// "Hosts File Disabled" — unlike "Default", which always has content
    /// (even if it's just the Windows default), this turns hosts resolution off
    /// entirely: no profile is active, not even Default.
    /// </summary>
    public void DisableAll()
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        _profileSwitcher.DisableAll();
        _auditLogger.Log(AuditActionType.HostsFileDisabled, AuditSource.MainForm);
    }

    private async void OnHostsFileDisabledClick(object sender, EventArgs e)
    {
        if (_profileSwitcher.IsHostsDisabled)
            await RestoreToDefault();
        else
            DisableAll();
    }

    private void PopulateProfilesMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();

        var menuSaveCurrent = new ToolStripMenuItem("Save Current as Profile…") { Enabled = !_profileSwitcher.IsHostsDisabled };
        menuSaveCurrent.Click += OnSaveCurrentAsProfileClick;
        parent.DropDownItems.Add(menuSaveCurrent);

        var menuNewEmpty = new ToolStripMenuItem("New Empty Profile…") { Enabled = !_profileSwitcher.IsHostsDisabled };
        menuNewEmpty.Click += OnNewEmptyProfileClick;
        parent.DropDownItems.Add(menuNewEmpty);

        var menuExport = new ToolStripMenuItem("Export Profiles…");
        menuExport.Click += OnExportProfilesClick;
        parent.DropDownItems.Add(menuExport);

        var menuImport = new ToolStripMenuItem("Import Profiles…");
        menuImport.Click += OnImportProfilesClick;
        parent.DropDownItems.Add(menuImport);

        parent.DropDownItems.Add(new ToolStripSeparator());

        var defaultProfile = GetDefaultProfile();
        bool isDefaultActive = _profileSwitcher.ActiveProfile == defaultProfile && !_profileSwitcher.IsHostsDisabled;
        var defaultFontStyle = isDefaultActive ? (FontStyle.Italic | FontStyle.Bold) : FontStyle.Italic;
        var menuDefault = BuildProfileMenuItem(parent, defaultProfile, "Default", new Font(parent.Font, defaultFontStyle),
            isDefaultActive, includeDelete: false);
        parent.DropDownItems.Add(menuDefault);

        parent.DropDownItems.Add(new ToolStripSeparator());

        // ── User-created profiles ──────────────────────────────────────────
        var profiles = _profileList
            .Where(a => !a.IsDefault)
            .OrderBy(a => a.Metadata?.SortOrder ?? 0)
            .ThenBy(a => a.FileName)
            .ToList();

        foreach (var profile in profiles)
        {
            bool isActive = _profileSwitcher.ActiveProfile == profile && !_profileSwitcher.IsHostsDisabled;
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
            Checked = _profileSwitcher.IsHostsDisabled,
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
            Enabled = !_profileSwitcher.IsHostsDisabled,
            Font = font
        };

        bool exists = File.Exists(profile.FilePath);
        var captured = profile;

        // Already active: re-activating just discards unsaved grid edits and
        // reloads from this profile's file — relabel so that's clear, rather
        // than reading as a redundant "activate the thing that's already active".
        var menuActivate = new ToolStripMenuItem(isActive ? "Reload" : "Activate") { Enabled = exists, Font = parent.Font };
        menuActivate.Click += async (_, _) =>
            await _profileSwitcher.ActivateAsync(captured, ProfileSwitcher.TriggerSource.TrayMenu);

        var menuClone = new ToolStripMenuItem("Clone…") { Enabled = exists, Font = parent.Font };
        menuClone.Click += (_, _) => OnCloneProfileClick(captured);

        var menuRawEditProfile = new ToolStripMenuItem("Raw Edit…") { Enabled = exists, Font = parent.Font };
        menuRawEditProfile.Click += (_, _) => OnProfileRawEditClick(captured);

        var menuSettings = new ToolStripMenuItem("Profile Settings…") { Font = parent.Font };
        menuSettings.Click += (_, _) =>
        {
            using var dlg = new ProfileSettingsForm(captured, _hotkeyRegistry);
            dlg.ShowDialog(_owner);
            Rebuild(); // color/sort-order/description changes don't fire ListChanged
        };

        // Doesn't make sense for the profile that's already active — there's
        // nothing to "temporarily switch to", you're already on it.
        var menuActivateTimer = new ToolStripMenuItem("Activate Temporarily…")
        {
            Enabled = exists,
            Visible = !isActive,
            Font = parent.Font
        };
        menuActivateTimer.Click += (_, _) => _activateWithTimer(captured);

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
        inputDialog.Text = _owner.Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;

        if (inputDialog.ShowDialog(_owner) == DialogResult.OK)
        {
            // Flush any in-progress grid edit so the saved profile matches what's on screen
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

            HostsFile.Instance.SaveAsProfile(inputDialog.Input);
        }
    }

    private void OnNewEmptyProfileClick(object? sender, EventArgs e)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = _owner.Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;

        if (inputDialog.ShowDialog(_owner) == DialogResult.OK)
        {
            var profile = new HostsProfile(inputDialog.Input);

            Directory.CreateDirectory(HostsProfileList.ProfileDirectory);

            // Write default Windows hosts content
            File.WriteAllText(profile.FilePath, Properties.Resources.hosts);

            _profileList.Add(profile);
        }
    }

    private async void OnCloneProfileClick(HostsProfile source)
    {
        using var inputDialog = new InputForm();
        inputDialog.Text = _owner.Text;
        inputDialog.Prompt = Properties.Resources.InputProfilePrompt;

        if (inputDialog.ShowDialog(_owner) == DialogResult.OK)
        {
            // If cloning the profile currently loaded on screen, sync its saved file
            // with the live (possibly edited, uncommitted) grid contents first
            if (source == _profileSwitcher.ActiveProfile)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                HostsFile.Instance.SaveAs(source.FilePath);
                _notifyActiveProfileFileWritten();
            }

            var destProfile = new HostsProfile(inputDialog.Input);
            File.Copy(source.FilePath, destProfile.FilePath);
            _profileList.Add(destProfile);

            // Clone is identical to the source at this point, so the diff-before-switch
            // dialog (if enabled) will skip itself and this activates silently
            await _profileSwitcher.ActivateAsync(destProfile, ProfileSwitcher.TriggerSource.TrayMenu);
        }
    }

    private async void OnDeleteProfileClick(HostsProfile profile)
    {
        bool isActive = _profileSwitcher.ActiveProfile == profile;

        var prompt = isActive
            ? $"Delete profile '{profile.FileName}'?\n\nIt's currently active — the hosts file will be reset to the Windows default."
            : $"Delete profile '{profile.FileName}'?";

        var result = MessageBox.Show(
            _owner,
            prompt,
            _owner.Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            // Deleting the file out from under the active profile would otherwise leave
            // the grid/live hosts file showing an orphaned copy of content that no
            // longer corresponds to anything — reset to a well-defined state instead.
            if (isActive)
                await RestoreToDefault();

            _profileList.Delete(profile);
        }
    }

    private void OnProfileRawEditClick(HostsProfile profile)
    {
        if (!File.Exists(profile.FilePath))
        {
            MessageBox.Show(_owner, $"Profile file not found:\n{profile.FilePath}",
                _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        HostsEntryList entries;
        try
        {
            entries = new HostsEntryList(new Utilities.UndoManager(), File.ReadAllLines(profile.FilePath), filterDefault: false);
        }
        catch (IOException ex)
        {
            MessageBox.Show(_owner, $"Could not read profile:\n{ex.Message}",
                _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var rawText = RawEditForm.FormatAligned(entries);

        using var form = new RawEditForm(rawText) { Icon = _owner.Icon, Text = $"Raw Edit — {profile.FileName}" };
        if (form.ShowDialog(_owner) != DialogResult.OK) return;

        // Writes only the profile's own file — never the live hosts file or grid.
        // If this happens to be the active profile, the existing external-change
        // watcher will naturally offer to reload it into the grid on next focus.
        File.WriteAllLines(profile.FilePath, form.GetLines());
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

    internal static Bitmap CreateColorSwatch(Color c, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(new SolidBrush(c), 0, 0, w, h);
        g.DrawRectangle(new Pen(Color.FromArgb(100, 0, 0, 0)), 0, 0, w - 1, h - 1);
        return bmp;
    }

    private void OnExportProfilesClick(object? sender, EventArgs e)
    {
        using var form = new ProfileExportForm(_profileList);
        if (form.ShowDialog(_owner) != DialogResult.OK) return;
        if (form.SelectedProfiles.Count == 0) return;

        using var saveDialog = new SaveFileDialog
        {
            Title = "Export Profiles",
            Filter = "Profile Package (*.zip)|*.zip",
            DefaultExt = "zip",
            FileName = "profiles-export"
        };
        if (saveDialog.ShowDialog(_owner) != DialogResult.OK) return;

        try
        {
            var bundleConfigFor = form.BundleConfigFiles
                ? form.SelectedProfiles
                    .Where(p => p.Metadata?.HasConfigFile == true)
                    .Select(p => p.FileName)
                    .ToHashSet()
                : (IReadOnlySet<string>)new HashSet<string>();

            _exportImportService.Export(saveDialog.FileName, form.SelectedProfiles, bundleConfigFor);

            MessageBox.Show(_owner,
                $"Exported {form.SelectedProfiles.Count} profile(s) to:\n{saveDialog.FileName}",
                _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_owner, $"Export failed:\n{ex.Message}", _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportProfilesClick(object? sender, EventArgs e)
    {
        using var openDialog = new OpenFileDialog
        {
            Title = "Import Profiles",
            Filter = "Profile Package (*.zip)|*.zip|All Files (*.*)|*.*"
        };
        if (openDialog.ShowDialog(_owner) != DialogResult.OK) return;

        ProfileExportManifest manifest;
        try
        {
            manifest = _exportImportService.ReadManifest(openDialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_owner, $"Could not read package:\n{ex.Message}", _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var importForm = new ProfileImportForm(manifest);
        if (importForm.ShowDialog(_owner) != DialogResult.OK) return;
        if (importForm.SelectedFileNames.Count == 0) return;

        List<string> notes;
        try
        {
            notes = _exportImportService.Import(openDialog.FileName, importForm.SelectedFileNames);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_owner, $"Import failed:\n{ex.Message}", _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var successMsg = $"Imported {importForm.SelectedFileNames.Count} profile(s).";
        if (notes.Count > 0)
            successMsg += "\n\n" + string.Join("\n\n", notes);

        MessageBox.Show(_owner, successMsg, _owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
