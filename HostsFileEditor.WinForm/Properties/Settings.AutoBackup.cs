namespace HostsFileEditor.Properties;

partial class Settings
{
    [global::System.Configuration.UserScopedSettingAttribute()]
    [global::System.Configuration.DefaultSettingValueAttribute("True")]
    public bool AutoBackupEnabled
    {
        get => (bool)(this["AutoBackupEnabled"] ?? true);
        set => this["AutoBackupEnabled"] = value;
    }

    [global::System.Configuration.UserScopedSettingAttribute()]
    [global::System.Configuration.DefaultSettingValueAttribute("20")]
    public int AutoBackupMaxCount
    {
        get => (int)(this["AutoBackupMaxCount"] ?? 20);
        set => this["AutoBackupMaxCount"] = value;
    }
}
