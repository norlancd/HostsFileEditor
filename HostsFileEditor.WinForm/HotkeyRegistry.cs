using HostsFileEditor.Win32;

namespace HostsFileEditor;

internal static class HotkeyRegistry
{
    private static IntPtr _hwnd = IntPtr.Zero;
    private static readonly Dictionary<int, HostsArchive> _idToArchive = [];
    private static int _nextId = 1;

    public static void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        RegisterAll();
    }

    public static void RegisterAll()
    {
        UnregisterAll();
        foreach (var archive in HostsArchiveList.Instance)
        {
            Register(archive);
        }
    }

    public static void Register(HostsArchive archive)
    {
        var metadata = archive.Metadata;
        if (metadata == null || !metadata.HasHotkey)
            return;

        // Check for duplicate chord already registered
        if (_idToArchive.Values.Any(a =>
            a != archive &&
            a.Metadata?.HotkeyModifiers == metadata.HotkeyModifiers &&
            a.Metadata?.HotkeyKey == metadata.HotkeyKey))
        {
            return;
        }

        int id = _nextId++;
        bool ok = NativeMethods.RegisterHotKey(_hwnd, id, (uint)metadata.HotkeyModifiers, (uint)metadata.HotkeyKey);
        if (ok)
        {
            _idToArchive[id] = archive;
        }
        else
        {
            HotkeyConflictNotify?.Invoke(archive, metadata);
        }
    }

    public static void Unregister(HostsArchive archive)
    {
        var pair = _idToArchive.FirstOrDefault(kv => kv.Value == archive);
        if (pair.Value != null)
        {
            NativeMethods.UnregisterHotKey(_hwnd, pair.Key);
            _idToArchive.Remove(pair.Key);
        }
    }

    public static void UnregisterAll()
    {
        foreach (var id in _idToArchive.Keys)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _idToArchive.Clear();
    }

    public static HostsArchive? GetProfileById(int id) =>
        _idToArchive.TryGetValue(id, out var archive) ? archive : null;

    public static bool IsChordTaken(int modifiers, int key, HostsArchive? excludeArchive = null) =>
        _idToArchive.Values.Any(a =>
            a != excludeArchive &&
            a.Metadata?.HotkeyModifiers == modifiers &&
            a.Metadata?.HotkeyKey == key);

    // Raised when a hotkey registration fails due to a system conflict
    public static event Action<HostsArchive, HostsProfileMetadata>? HotkeyConflictNotify;
}
