using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostsFileEditor;

public class HostsProfileMetadata
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
            return JsonSerializer.Deserialize<HostsProfileMetadata>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string archiveFilePath)
    {
        var sidecarPath = GetSidecarPath(archiveFilePath);
        var json = JsonSerializer.Serialize(this, _jsonOptions);
        File.WriteAllText(sidecarPath, json);
    }

    public void Delete(string archiveFilePath)
    {
        var sidecarPath = GetSidecarPath(archiveFilePath);
        if (File.Exists(sidecarPath))
            File.Delete(sidecarPath);
    }
}
