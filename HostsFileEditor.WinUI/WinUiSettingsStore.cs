namespace HostsFileEditor;

/// <summary>
/// Backs <see cref="ProfileSwitcher"/>'s persistence with WinUI's own
/// <see cref="LocalSettings"/> (a local JSON file) — the WinUI-specific half of
/// the abstraction that lets <see cref="ProfileSwitcher"/> live in Core.
/// </summary>
internal sealed class WinUiSettingsStore : ISettingsStore
{
    public string? ActiveProfileName
    {
        get => LocalSettings.GetString("ActiveProfileName");
        set => LocalSettings.SetString("ActiveProfileName", value);
    }

    public bool HostsDisabled
    {
        get => LocalSettings.GetBool("HostsDisabled", defaultValue: false);
        set => LocalSettings.SetBool("HostsDisabled", value);
    }

    public bool DiffBeforeSwitchEnabled
    {
        get => LocalSettings.GetBool("DiffBeforeSwitchEnabled", defaultValue: false);
        set => LocalSettings.SetBool("DiffBeforeSwitchEnabled", value);
    }

    // LocalSettings persists immediately on every Set call — nothing to flush here.
    public void Save() { }
}
