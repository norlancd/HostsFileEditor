using HostsFileEditor.Properties;

namespace HostsFileEditor;

/// <summary>
/// Backs <see cref="ProfileSwitcher"/>'s persistence with WinForm's own
/// <see cref="Settings"/> (<c>ApplicationSettingsBase</c>) — the WinForm-specific
/// half of the abstraction that lets <see cref="ProfileSwitcher"/> live in Core.
/// </summary>
internal sealed class WinFormsSettingsStore : ISettingsStore
{
    public string? ActiveProfileName
    {
        get => Settings.Default.ActiveProfileName;
        set => Settings.Default.ActiveProfileName = value ?? string.Empty;
    }

    public bool HostsDisabled
    {
        get => Settings.Default.HostsDisabled;
        set => Settings.Default.HostsDisabled = value;
    }

    public bool DiffBeforeSwitchEnabled
    {
        get => Settings.Default.DiffBeforeSwitchEnabled;
        set => Settings.Default.DiffBeforeSwitchEnabled = value;
    }

    public void Save() => Settings.Default.Save();
}
