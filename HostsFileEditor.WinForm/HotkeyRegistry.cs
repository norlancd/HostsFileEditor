using HostsFileEditor.Win32;
using System.Runtime.InteropServices;

namespace HostsFileEditor;

internal static class HotkeyRegistry
{
    private static IntPtr _hwnd = IntPtr.Zero;
    private static readonly Dictionary<int, HostsProfile> _idToProfile = [];
    private static int _nextId = 1;

    public static void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        RegisterAll();
    }

    public static void RegisterAll()
    {
        UnregisterAll();
        foreach (var profile in HostsProfileList.Instance)
        {
            Register(profile);
        }
    }

    public static void Register(HostsProfile profile)
    {
        var metadata = profile.Metadata;
        if (metadata == null || !metadata.HasHotkey)
            return;

        // Check for duplicate chord already registered
        if (_idToProfile.Values.Any(a =>
            a != profile &&
            a.Metadata?.HotkeyModifiers == metadata.HotkeyModifiers &&
            a.Metadata?.HotkeyKey == metadata.HotkeyKey))
        {
            return;
        }

        int id = _nextId++;
        bool ok = NativeMethods.RegisterHotKey(_hwnd, id, (uint)metadata.HotkeyModifiers, (uint)metadata.HotkeyKey);
        if (ok)
        {
            _idToProfile[id] = profile;
        }
        else
        {
            HotkeyConflictNotify?.Invoke(profile, metadata);
        }
    }

    public static void Unregister(HostsProfile profile)
    {
        var pair = _idToProfile.FirstOrDefault(kv => kv.Value == profile);
        if (pair.Value != null)
        {
            NativeMethods.UnregisterHotKey(_hwnd, pair.Key);
            _idToProfile.Remove(pair.Key);
        }
    }

    public static void UnregisterAll()
    {
        foreach (var id in _idToProfile.Keys)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _idToProfile.Clear();
    }

    public static HostsProfile? GetProfileById(int id) =>
        _idToProfile.TryGetValue(id, out var profile) ? profile : null;

    public static bool IsChordTaken(int modifiers, int key, HostsProfile? excludeProfile = null) =>
        _idToProfile.Values.Any(a =>
            a != excludeProfile &&
            a.Metadata?.HotkeyModifiers == modifiers &&
            a.Metadata?.HotkeyKey == key);

    /// <summary>
    /// Synchronously attempts to (re)assign <paramref name="profile"/>'s global hotkey to
    /// the given chord, so the caller can find out — before committing anything to disk or
    /// closing a dialog — whether it's actually available, rather than discovering a failure
    /// later via <see cref="HotkeyConflictNotify"/> after the fact. Passing key=0 clears the
    /// profile's hotkey. On failure, any previous registration for this profile is restored.
    /// </summary>
    public static bool TryAssignHotkey(HostsProfile profile, int modifiers, int key, out string? error)
    {
        error = null;

        if (key == 0)
        {
            Unregister(profile);
            return true;
        }

        if (IsChordTaken(modifiers, key, profile))
        {
            error = "This hotkey is already assigned to another profile.";
            return false;
        }

        // Remember the profile's current registration (if any) so it can be restored on failure
        var existing = _idToProfile.FirstOrDefault(kv => kv.Value == profile);
        bool hadExisting = existing.Value != null;
        int existingId = existing.Key;
        int existingModifiers = profile.Metadata?.HotkeyModifiers ?? 0;
        int existingKey = profile.Metadata?.HotkeyKey ?? 0;

        if (hadExisting)
        {
            NativeMethods.UnregisterHotKey(_hwnd, existingId);
            _idToProfile.Remove(existingId);
        }

        int newId = _nextId++;
        bool ok = NativeMethods.RegisterHotKey(_hwnd, newId, (uint)modifiers, (uint)key);
        const int ErrorHotkeyAlreadyRegistered = 1409;
        int win32Error = ok ? 0 : Marshal.GetLastWin32Error();

        if (ok)
        {
            _idToProfile[newId] = profile;
            return true;
        }

        // Failed — put the old registration back so state isn't left inconsistent
        if (hadExisting && NativeMethods.RegisterHotKey(_hwnd, existingId, (uint)existingModifiers, (uint)existingKey))
        {
            _idToProfile[existingId] = profile;
        }

        error = win32Error == ErrorHotkeyAlreadyRegistered
            ? "This key combination is already in use by another application."
            : $"Could not register this hotkey ({new System.ComponentModel.Win32Exception(win32Error).Message}).";
        return false;
    }

    // Raised when a hotkey registration fails due to a system conflict
    public static event Action<HostsProfile, HostsProfileMetadata>? HotkeyConflictNotify;
}
