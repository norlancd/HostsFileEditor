namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="HotkeyRegistry"/>'s instance surface — extracted so
/// consumers depend on this via constructor injection instead of the static class
/// member access <see cref="HotkeyRegistry"/> used to be.
/// </summary>
public interface IHotkeyRegistry
{
    void Initialize(IntPtr hwnd);

    void RegisterAll();

    void Register(HostsProfile profile);

    void Unregister(HostsProfile profile);

    void UnregisterAll();

    HostsProfile? GetProfileById(int id);

    bool IsChordTaken(int modifiers, int key, HostsProfile? excludeProfile = null);

    /// <summary>
    /// Synchronously attempts to (re)assign <paramref name="profile"/>'s global hotkey to
    /// the given chord, so the caller can find out — before committing anything to disk or
    /// closing a dialog — whether it's actually available, rather than discovering a failure
    /// later via <see cref="HotkeyConflictNotify"/> after the fact. Passing key=0 clears the
    /// profile's hotkey. On failure, any previous registration for this profile is restored.
    /// </summary>
    bool TryAssignHotkey(HostsProfile profile, int modifiers, int key, out string? error);

    // Raised when a hotkey registration fails due to a system conflict
    event Action<HostsProfile, HostsProfileMetadata>? HotkeyConflictNotify;
}
