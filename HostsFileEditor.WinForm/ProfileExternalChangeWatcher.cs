namespace HostsFileEditor;

/// <summary>
/// Watches for the live hosts file or the active profile's file being modified
/// outside this app, checking whenever the owner window regains focus (mirrors how
/// editors like Notepad++ flag a file changed on disk). Extracted out of MainForm
/// so this concern owns its own state explicitly instead of living in form fields
/// alongside unrelated clipboard/menu/audit responsibilities.
/// </summary>
internal sealed class ProfileExternalChangeWatcher : IDisposable
{
    private readonly Form _owner;
    private readonly Action _onReloaded;
    private readonly IProfileSwitcher _profileSwitcher;

    // Last write time of the active profile's file as far as THIS app knows —
    // updated whenever we activate a profile or write to it ourselves (Save sync,
    // Clone sync). If the file's actual write time on disk is ever newer than this,
    // something external touched it.
    private DateTime? _activeProfileLastKnownWriteUtc;

    /// <param name="owner">
    /// Owner window for the Yes/No prompts; its Activated event drives the check.
    /// </param>
    /// <param name="onReloaded">
    /// Called after the user accepts reloading from disk, so the caller can
    /// re-baseline anything it tracks itself (e.g. the audit change-tracker).
    /// </param>
    public ProfileExternalChangeWatcher(Form owner, Action onReloaded, IProfileSwitcher profileSwitcher)
    {
        _owner = owner;
        _onReloaded = onReloaded;
        _profileSwitcher = profileSwitcher;

        _profileSwitcher.ActiveProfileChanged += OnActiveProfileChanged;
        _owner.Activated += OnOwnerActivated;

        OnActiveProfileChanged();
    }

    public void Dispose()
    {
        _profileSwitcher.ActiveProfileChanged -= OnActiveProfileChanged;
        _owner.Activated -= OnOwnerActivated;
    }

    /// <summary>
    /// Call this immediately after the app itself writes to the active profile's
    /// file (e.g. Save syncing to it, or Clone syncing it before copying), so the
    /// next external-change check doesn't mistake our own write for an external one.
    /// </summary>
    public void NotifyActiveProfileFileWritten() =>
        _activeProfileLastKnownWriteUtc = GetActiveProfileWriteTimeUtc();

    private void OnActiveProfileChanged() =>
        _activeProfileLastKnownWriteUtc = GetActiveProfileWriteTimeUtc();

    private DateTime? GetActiveProfileWriteTimeUtc()
    {
        var active = _profileSwitcher.ActiveProfile;
        if (active == null || !File.Exists(active.FilePath)) return null;

        try { return File.GetLastWriteTimeUtc(active.FilePath); }
        catch (IOException) { return null; }
    }

    private void OnOwnerActivated(object? sender, EventArgs e)
    {
        // Hosts file first (the one that's actually live), then the active profile —
        // mirrors how Notepad++ flags a file changed on disk as soon as you switch
        // back into the window, instead of waiting until you try to save.
        CheckHostsFileExternalChange();
        CheckActiveProfileExternalChange();
    }

    private void CheckHostsFileExternalChange()
    {
        if (!HostsFile.Instance.WasModifiedExternally()) return;

        var pendingNote = HostsFile.Instance.HasUnsavedChanges
            ? "\n\nYou have unsaved changes on screen — they will be discarded if you reload."
            : string.Empty;

        var result = MessageBox.Show(
            _owner,
            $"The hosts file was modified outside this application.{pendingNote}\n\nReload it now?",
            _owner.Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            HostsFile.Instance.Refresh();
            _onReloaded();
        }
        else
        {
            // Don't keep nagging about the same already-dismissed edit on every
            // subsequent window activation — only re-prompt if it changes again.
            HostsFile.Instance.AcknowledgeExternalChange();
        }
    }

    private void CheckActiveProfileExternalChange()
    {
        var active = _profileSwitcher.ActiveProfile;
        if (active == null || !File.Exists(active.FilePath)) return;

        DateTime currentWriteUtc;
        try { currentWriteUtc = File.GetLastWriteTimeUtc(active.FilePath); }
        catch (IOException) { return; }

        var known = _activeProfileLastKnownWriteUtc;
        if (known != null && currentWriteUtc <= known.Value) return; // no external change

        // Remember this version going forward regardless of what the user chooses,
        // so dismissing the prompt doesn't cause it to nag again for the same edit.
        _activeProfileLastKnownWriteUtc = currentWriteUtc;

        if (known == null) return; // first time tracking this profile — nothing to compare yet

        var pendingNote = HostsFile.Instance.HasUnsavedChanges
            ? "\n\nYou have unsaved changes on screen — they will be discarded if you reload."
            : string.Empty;

        var result = MessageBox.Show(
            _owner,
            $"The active profile \"{active.FileName}\" was modified outside this application.{pendingNote}\n\nReload it now?",
            _owner.Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            HostsFile.Instance.Import(active.FilePath, removeDefaultTextOverride: active.IsDefault ? false : null);
            _onReloaded();
        }
        // else: keep current in-memory state as-is. If there were unsaved changes,
        // the status bar's "Unsaved changes" indicator already communicates that.
    }
}
