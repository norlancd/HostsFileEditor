namespace HostsFileEditor;

/// <summary>One profile's record inside an export package's manifest.json.</summary>
public class ProfileExportManifestEntry
{
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Informational only — the config-copy paths as they were on the exporting
    /// machine. Never re-applied automatically on import (they're machine-specific
    /// and the user importing didn't type them in themselves), but shown to the user
    /// as a reminder of what used to be configured.
    /// </summary>
    public string? OriginalConfigSourcePath { get; set; }

    public string? OriginalConfigDestinationPath { get; set; }

    /// <summary>True if the actual config file's contents were bundled into the package
    /// (an explicit opt-in at export time, since that file may hold secrets).</summary>
    public bool ConfigFileBundled { get; set; }
}

/// <summary>The manifest.json at the root of a profile export .zip.</summary>
public class ProfileExportManifest
{
    public int FormatVersion { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public string AppVersion { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public List<ProfileExportManifestEntry> Profiles { get; set; } = [];
}
