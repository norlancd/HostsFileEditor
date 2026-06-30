namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="ProfileSwitcher"/>'s instance surface — extracted so
/// consumers depend on this via constructor injection instead of static member access.
/// </summary>
public interface IProfileSwitcher
{
    HostsProfile? ActiveProfile { get; }

    /// <summary>
    /// True when hosts resolution itself has been switched off (no profile active,
    /// not even Default — Windows has no hosts file at all). Mutually exclusive
    /// with <see cref="ActiveProfile"/>; switching to any profile or Default clears it.
    /// </summary>
    bool IsHostsDisabled { get; }

    event Action? ActiveProfileChanged;

    /// <returns>True if the switch actually happened (false if the file was missing or the diff was cancelled).</returns>
    Task<bool> ActivateAsync(HostsProfile profile, ProfileSwitcher.TriggerSource source);

    void ClearActive();

    /// <summary>
    /// Disables hosts resolution entirely — the "no profile, not even Default" state.
    /// </summary>
    void DisableAll();

    /// <summary>
    /// Restores tracking of the active profile at startup without re-importing or
    /// saving to the hosts file (the hosts file already reflects this profile).
    /// </summary>
    void RestoreActive(HostsProfile? profile);

    /// <summary>
    /// Restores tracking of the "hosts disabled" state at startup, without touching
    /// disk (the hosts file is already absent from a previous session).
    /// </summary>
    void RestoreDisabled();

    /// <summary>
    /// Resolves which profile was active in a previous session — by saved name
    /// first, falling back to comparing file contents if it no longer matches
    /// (e.g. profile renamed/deleted, or hosts file edited outside this app) — and
    /// restores tracking accordingly. Call once at startup.
    /// </summary>
    void RestoreFromSettings();

    /// <summary>
    /// Re-syncs which profile is tracked as active after something wrote the live
    /// hosts file directly, bypassing <see cref="Activate"/> (e.g. a rollback-timer
    /// revert) — without this, the Profiles menu checkmark and status bar would
    /// keep pointing at whatever was active before that direct write.
    /// </summary>
    void SyncActiveAfterExternalWrite();

    // Set by the UI to display tray/toast errors
    Action<string>? ProfileError { get; set; }

    // Set by the diff-before-switch integration to show a confirmation dialog; returns true to confirm switch
    Func<HostsProfile, Task<bool>>? DiffBeforeSwitch { get; set; }
}
