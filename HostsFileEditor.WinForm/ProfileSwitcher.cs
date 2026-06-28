using HostsFileEditor.Properties;

namespace HostsFileEditor;

internal static class ProfileSwitcher
{
    public enum TriggerSource { TrayHotkey, TrayMenu }

    public static HostsProfile? ActiveProfile { get; private set; }

    /// <summary>
    /// True when hosts resolution itself has been switched off (no profile active,
    /// not even Default — Windows has no hosts file at all). Mutually exclusive
    /// with <see cref="ActiveProfile"/>; switching to any profile or Default clears it.
    /// </summary>
    public static bool IsHostsDisabled { get; private set; }

    public static event Action? ActiveProfileChanged;

    /// <returns>True if the switch actually happened (false if the file was missing or the diff was cancelled).</returns>
    public static bool Activate(HostsProfile profile, TriggerSource source)
    {
        if (!File.Exists(profile.FilePath))
        {
            ProfileError?.Invoke(string.Format(Resources.ProfileFileNotFound, profile.FileName));
            return false;
        }

        if (Settings.Default.DiffBeforeSwitchEnabled)
        {
            // SPEC-02 diff dialog hook — invoke if handler is registered
            if (DiffBeforeSwitch != null)
            {
                bool confirmed = DiffBeforeSwitch(profile);
                if (!confirmed)
                    return false;
            }
        }

        // Default's whole file IS the canonical default content, so the
        // RemoveDefaultText filter would always strip every line — bypass it here,
        // same as the legacy RestoreDefault() behavior this replaced.
        HostsFile.Instance.Import(profile.FilePath, removeDefaultTextOverride: profile.IsDefault ? false : null);
        HostsFile.Instance.Save();

        SetActive(profile);
        return true;
    }

    public static void ClearActive() => SetActive(null);

    /// <summary>
    /// Disables hosts resolution entirely — the "no profile, not even Default" state.
    /// </summary>
    public static void DisableAll()
    {
        HostsFile.Instance.DisableAll();
        SetActive(null, disabled: true);
    }

    private static void SetActive(HostsProfile? profile, bool disabled = false)
    {
        ActiveProfile = profile;
        IsHostsDisabled = disabled;
        Settings.Default.ActiveProfileName = profile?.FileName ?? string.Empty;
        Settings.Default.HostsDisabled = disabled;
        Settings.Default.Save();
        ActiveProfileChanged?.Invoke();
    }

    /// <summary>
    /// Restores tracking of the active profile at startup without re-importing or
    /// saving to the hosts file (the hosts file already reflects this profile).
    /// </summary>
    public static void RestoreActive(HostsProfile? profile)
    {
        ActiveProfile = profile;
        IsHostsDisabled = false;
        ActiveProfileChanged?.Invoke();
    }

    /// <summary>
    /// Restores tracking of the "hosts disabled" state at startup, without touching
    /// disk (the hosts file is already absent from a previous session).
    /// </summary>
    public static void RestoreDisabled()
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
    public static void RestoreFromSettings()
    {
        if (!HostsFile.IsEnabled)
        {
            RestoreDisabled();
            return;
        }

        var savedName = Settings.Default.ActiveProfileName;

        var match = !string.IsNullOrEmpty(savedName)
            ? HostsProfileList.Instance.FirstOrDefault(a => a.FileName == savedName)
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
    public static void SyncActiveAfterExternalWrite()
    {
        var match = DetectActiveByContent();
        if (match != null)
            RestoreActive(match);
        else
            ClearActive();
    }

    private static HostsProfile? DetectActiveByContent()
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

    // Set by MainForm to display tray balloon errors
    public static Action<string>? ProfileError { get; set; }

    // Set by SPEC-02 integration to show diff dialog; returns true to confirm switch
    public static Func<HostsProfile, bool>? DiffBeforeSwitch { get; set; }
}
