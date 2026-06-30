using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostsFileEditor;

/// <summary>A source→destination file copy that runs when a profile activates.</summary>
public class FileReplacement
{
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(DestinationPath);
}

public enum ProfileCommandTiming { Before, After }

/// <summary>A shell command to run before or after a profile switch.</summary>
public class ProfileCommand
{
    [JsonPropertyName("executable")]
    public string Executable { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("timing")]
    public ProfileCommandTiming Timing { get; set; } = ProfileCommandTiming.After;

    [JsonPropertyName("waitForExit")]
    public bool WaitForExit { get; set; } = true;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Executable);
}

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

    /// <summary>File copies to apply on profile activation. Replaces the legacy single-pair fields.</summary>
    [JsonPropertyName("fileReplacements")]
    public List<FileReplacement> FileReplacements { get; set; } = [];

    /// <summary>Shell commands to run before or after a profile switch.</summary>
    [JsonPropertyName("commands")]
    public List<ProfileCommand> Commands { get; set; } = [];

    // Kept for backward-compat reading of old sidecar files. Superseded by FileReplacements.
    [JsonPropertyName("configSourcePath")]
    public string ConfigSourcePath { get; set; } = string.Empty;

    [JsonPropertyName("configDestinationPath")]
    public string ConfigDestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Returns the effective file replacements, migrating the legacy single-pair fields
    /// if the new list is empty and the old fields are set.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<FileReplacement> EffectiveFileReplacements
    {
        get
        {
            if (FileReplacements.Count > 0) return FileReplacements;
            if (!string.IsNullOrWhiteSpace(ConfigSourcePath) && !string.IsNullOrWhiteSpace(ConfigDestinationPath))
                return [new FileReplacement { SourcePath = ConfigSourcePath, DestinationPath = ConfigDestinationPath }];
            return [];
        }
    }

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
