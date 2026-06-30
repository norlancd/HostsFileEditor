namespace HostsFileEditor;

/// <summary>
/// Persistence abstraction for the handful of settings <see cref="ProfileSwitcher"/>
/// needs to survive a restart. Each UI project backs this with its own settings
/// mechanism (WinForm: <c>ApplicationSettingsBase</c>; WinUI: a local JSON file) —
/// this is what let <see cref="ProfileSwitcher"/> move into Core and be shared by both.
/// </summary>
public interface ISettingsStore
{
    string? ActiveProfileName { get; set; }

    bool HostsDisabled { get; set; }

    bool DiffBeforeSwitchEnabled { get; set; }

    void Save();
}
