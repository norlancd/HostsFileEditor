using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostsFileEditor;

public class HostsProfileMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // Maps to System.Windows.Forms.Keys flags (Alt=1, Control=2, Shift=4)
    [JsonPropertyName("hotkeyModifiers")]
    public int HotkeyModifiers { get; set; }

    // Maps to System.Windows.Forms.Keys enum integer value
    [JsonPropertyName("hotkeyKey")]
    public int HotkeyKey { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Path to a file that holds the config needed to interact with this profile's
    /// servers (e.g. a VPN/SSH/kubeconfig). When set together with
    /// <see cref="ConfigDestinationPath"/>, activating this profile copies it into
    /// place automatically. Either or both blank means "do nothing" — most profiles
    /// won't use this.
    /// </summary>
    [JsonPropertyName("configSourcePath")]
    public string ConfigSourcePath { get; set; } = string.Empty;

    /// <summary>The file <see cref="ConfigSourcePath"/> gets copied (overwritten) onto when this profile activates.</summary>
    [JsonPropertyName("configDestinationPath")]
    public string ConfigDestinationPath { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasConfigFile => !string.IsNullOrWhiteSpace(ConfigSourcePath) && !string.IsNullOrWhiteSpace(ConfigDestinationPath);

    [JsonIgnore]
    public bool HasHotkey => HotkeyKey != 0;

    public static string GetSidecarPath(string archiveFilePath) =>
        archiveFilePath + ".json";

    public static HostsProfileMetadata? Load(string archiveFilePath)
    {
        var sidecarPath = GetSidecarPath(archiveFilePath);
        if (!File.Exists(sidecarPath))
            return null;

        try
        {
            var json = File.ReadAllText(sidecarPath);
            return JsonSerializer.Deserialize(json, CoreJsonContext.Default.HostsProfileMetadata);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string archiveFilePath)
    {
        var sidecarPath = GetSidecarPath(archiveFilePath);
        var json = JsonSerializer.Serialize(this, CoreJsonContext.Default.HostsProfileMetadata);
        File.WriteAllText(sidecarPath, json);
    }

    public void Delete(string archiveFilePath)
    {
        var sidecarPath = GetSidecarPath(archiveFilePath);
        if (File.Exists(sidecarPath))
            File.Delete(sidecarPath);
    }
}
