using HostsFileEditor.Properties;

namespace HostsFileEditor;

internal static class ProfileSwitcher
{
    public enum TriggerSource { TrayHotkey, TrayMenu }

    public static HostsArchive? ActiveArchive { get; private set; }

    public static event Action? ActiveArchiveChanged;

    public static void Activate(HostsArchive archive, TriggerSource source)
    {
        if (!File.Exists(archive.FilePath))
        {
            ProfileError?.Invoke(string.Format(Resources.ProfileFileNotFound, archive.FileName));
            return;
        }

        if (Settings.Default.DiffBeforeSwitchEnabled)
        {
            // SPEC-02 diff dialog hook — invoke if handler is registered
            if (DiffBeforeSwitch != null)
            {
                bool confirmed = DiffBeforeSwitch(archive);
                if (!confirmed)
                    return;
            }
        }

        HostsFile.Instance.Import(archive.FilePath);
        HostsFile.Instance.Save();

        ActiveArchive = archive;
        ActiveArchiveChanged?.Invoke();
    }

    public static void ClearActive()
    {
        ActiveArchive = null;
        ActiveArchiveChanged?.Invoke();
    }

    // Set by MainForm to display tray balloon errors
    public static Action<string>? ProfileError { get; set; }

    // Set by SPEC-02 integration to show diff dialog; returns true to confirm switch
    public static Func<HostsArchive, bool>? DiffBeforeSwitch { get; set; }
}
