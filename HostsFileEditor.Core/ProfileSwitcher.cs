using HostsFileEditor.Properties;

namespace HostsFileEditor;

/// <summary>
/// Shared between WinForm and WinUI — moved into Core (off the WinForm-only static
/// class it used to be) so both UIs track "which profile is active" through the
/// exact same logic, instead of WinUI having no concept of an active profile at all.
/// Each UI's composition root owns the one instance and backs <see cref="ISettingsStore"/>
/// with its own persistence mechanism.
/// </summary>
public sealed class ProfileSwitcher : IProfileSwitcher
{
    public enum TriggerSource { TrayHotkey, TrayMenu }

    private readonly IHostsFile _hostsFile;
    private readonly IHostsProfileList _profileList;
    private readonly ISettingsStore _settings;
    private readonly IAuditLogger _auditLogger;

    public ProfileSwitcher(IHostsFile hostsFile, IHostsProfileList profileList, ISettingsStore settings, IAuditLogger auditLogger)
    {
        _hostsFile = hostsFile;
        _profileList = profileList;
        _settings = settings;
        _auditLogger = auditLogger;
    }

    public HostsProfile? ActiveProfile { get; private set; }

    /// <summary>
    /// True when hosts resolution itself has been switched off (no profile active,
    /// not even Default — Windows has no hosts file at all). Mutually exclusive
    /// with <see cref="ActiveProfile"/>; switching to any profile or Default clears it.
    /// </summary>
    public bool IsHostsDisabled { get; private set; }

    public event Action? ActiveProfileChanged;

    /// <returns>True if the switch actually happened (false if the file was missing or the diff was cancelled).</returns>
    public async Task<bool> ActivateAsync(HostsProfile profile, TriggerSource source)
    {
        if (!File.Exists(profile.FilePath))
        {
            ProfileError?.Invoke(string.Format(Resources.ProfileFileNotFound, profile.FileName));
            return false;
        }

        if (_settings.DiffBeforeSwitchEnabled)
        {
            // Diff-before-switch hook — invoke if a handler is registered
            if (DiffBeforeSwitch != null)
            {
                bool confirmed = await DiffBeforeSwitch(profile);
                if (!confirmed)
                    return false;
            }
        }

        var previousProfile = ActiveProfile;

        // Default's whole file IS the canonical default content, so the
        // RemoveDefaultText filter would always strip every line — bypass it here,
        // same as the legacy RestoreDefault() behavior this replaced.
        _hostsFile.Import(profile.FilePath, removeDefaultTextOverride: profile.IsDefault ? false : null);
        _hostsFile.Save();

        SetActive(profile);

        var auditSource = source == TriggerSource.TrayHotkey ? AuditSource.TrayHotkey : AuditSource.TrayMenu;

        _auditLogger.Log(
            AuditActionType.ProfileSwitch,
            auditSource,
            new AuditDetail
            {
                ProfileFrom = previousProfile?.FileName,
                ProfileTo = profile.FileName,
                BypassedDiff = !_settings.DiffBeforeSwitchEnabled
            });

        ApplyConfigFile(profile, auditSource);

        return true;
    }

    /// <summary>
    /// Copies a profile's configured "config file" (e.g. a VPN/SSH/kubeconfig needed
    /// to talk to that profile's servers) into place after activation. Both
    /// <see cref="HostsProfileMetadata.ConfigSourcePath"/> and
    /// <see cref="HostsProfileMetadata.ConfigDestinationPath"/> are opt-in per profile —
    /// most profiles leave them blank, in which case this does nothing.
    /// </summary>
    private void ApplyConfigFile(HostsProfile profile, string auditSource)
    {
        var metadata = profile.Metadata;
        if (metadata == null || !metadata.HasConfigFile) return;

        try
        {
            var destDir = Path.GetDirectoryName(metadata.ConfigDestinationPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(metadata.ConfigSourcePath, metadata.ConfigDestinationPath, overwrite: true);

            _auditLogger.Log(AuditActionType.ProfileConfigFileApplied, auditSource, new AuditDetail
            {
                ProfileTo = profile.FileName,
                SourcePath = metadata.ConfigSourcePath,
                DestinationPath = metadata.ConfigDestinationPath
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _auditLogger.Log(AuditActionType.ProfileConfigFileFailed, auditSource, new AuditDetail
            {
                ProfileTo = profile.FileName,
                SourcePath = metadata.ConfigSourcePath,
                DestinationPath = metadata.ConfigDestinationPath,
                NewValue = ex.Message
            });

            ProfileError?.Invoke($"Profile \"{profile.FileName}\" activated, but its config file could not be applied:\n{ex.Message}");
        }
    }

    public void ClearActive() => SetActive(null);

    /// <summary>
    /// Disables hosts resolution entirely — the "no profile, not even Default" state.
    /// </summary>
    public void DisableAll()
    {
        _hostsFile.DisableAll();
        SetActive(null, disabled: true);
    }

    private void SetActive(HostsProfile? profile, bool disabled = false)
    {
        ActiveProfile = profile;
        IsHostsDisabled = disabled;
        _settings.ActiveProfileName = profile?.FileName ?? string.Empty;
        _settings.HostsDisabled = disabled;
        _settings.Save();
        ActiveProfileChanged?.Invoke();
    }

    /// <summary>
    /// Restores tracking of the active profile at startup without re-importing or
    /// saving to the hosts file (the hosts file already reflects this profile).
    /// </summary>
    public void RestoreActive(HostsProfile? profile)
    {
        ActiveProfile = profile;
        IsHostsDisabled = false;
        ActiveProfileChanged?.Invoke();
    }

    /// <summary>
    /// Restores tracking of the "hosts disabled" state at startup, without touching
    /// disk (the hosts file is already absent from a previous session).
    /// </summary>
    public void RestoreDisabled()
    {
        ActiveProfile = null;
        IsHostsDisabled = true;
        ActiveProfileChanged?.Invoke();
    }

    /// <summary>
    /// Resolves which profile was active in a previous session — by saved name
    /// first, falling back to comparing file contents if it no longer matches
    /// (e.g. profile renamed/deleted, or hosts file edited outside this app) — and
    /// restores tracking accordingly. Call once at startup.
    /// </summary>
    public void RestoreFromSettings()
    {
        if (!HostsFile.IsEnabled)
        {
            RestoreDisabled();
            return;
        }

        var savedName = _settings.ActiveProfileName;

        var match = !string.IsNullOrEmpty(savedName)
            ? _profileList.FirstOrDefault(a => a.FileName == savedName)
            : null;

        match ??= DetectActiveByContent();

        if (match != null)
        {
            RestoreActive(match);
        }
    }

    /// <summary>
    /// Re-syncs which profile is tracked as active after something wrote the live
    /// hosts file directly, bypassing <see cref="Activate"/> (e.g. a rollback-timer
    /// revert) — without this, the Profiles menu checkmark and status bar would
    /// keep pointing at whatever was active before that direct write.
    /// </summary>
    public void SyncActiveAfterExternalWrite()
    {
        var match = DetectActiveByContent();
        if (match != null)
            RestoreActive(match);
        else
            ClearActive();
    }

    private HostsProfile? DetectActiveByContent()
    {
        try
        {
            var hostsLines = File.ReadAllLines(HostsFile.DefaultHostFilePath);

            foreach (var profile in _profileList)
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

    // Set by the UI to display tray/toast errors
    public Action<string>? ProfileError { get; set; }

    // Set by the diff-before-switch integration to show a confirmation dialog; returns true to confirm switch
    public Func<HostsProfile, Task<bool>>? DiffBeforeSwitch { get; set; }
}
