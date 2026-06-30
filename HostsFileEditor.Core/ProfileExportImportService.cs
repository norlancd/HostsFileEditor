using System.IO.Compression;
using System.Text.Json;

namespace HostsFileEditor;

/// <summary>
/// Exports a set of profiles (hosts content + sanitized metadata, optionally their
/// config-copy file contents) into a single portable .zip, and imports one back —
/// shared by both UIs since profile storage itself is a Core concern.
/// </summary>
public sealed class ProfileExportImportService : IProfileExportImportService
{
    private const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHostsProfileList _profileList;
    private readonly IAuditLogger _auditLogger;

    public ProfileExportImportService(IHostsProfileList profileList, IAuditLogger auditLogger)
    {
        _profileList = profileList;
        _auditLogger = auditLogger;
    }

    public void Export(string zipPath, IEnumerable<HostsProfile> profiles, IReadOnlySet<string> bundleConfigForFileNames)
    {
        var manifest = new ProfileExportManifest
        {
            AppVersion = typeof(ProfileExportImportService).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            MachineName = Environment.MachineName
        };

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var profile in profiles)
            {
                if (!File.Exists(profile.FilePath)) continue;

                zip.CreateEntryFromFile(profile.FilePath, $"profiles/{profile.FileName}");

                var manifestEntry = new ProfileExportManifestEntry { FileName = profile.FileName };
                var metadata = profile.Metadata;

                if (metadata != null)
                {
                    manifestEntry.OriginalConfigSourcePath = string.IsNullOrWhiteSpace(metadata.ConfigSourcePath) ? null : metadata.ConfigSourcePath;
                    manifestEntry.OriginalConfigDestinationPath = string.IsNullOrWhiteSpace(metadata.ConfigDestinationPath) ? null : metadata.ConfigDestinationPath;

                    // Sanitized sidecar — hotkeys (machine/user-specific global registration)
                    // and config-copy paths (covered by the manifest fields above instead,
                    // never re-applied automatically) are deliberately left at their defaults.
                    var exportMetadata = new HostsProfileMetadata
                    {
                        Name = metadata.Name,
                        Color = metadata.Color,
                        SortOrder = metadata.SortOrder,
                        Description = metadata.Description
                    };
                    WriteJsonEntry(zip, $"profiles/{profile.FileName}.json", exportMetadata);

                    if (bundleConfigForFileNames.Contains(profile.FileName) &&
                        metadata.HasConfigFile &&
                        File.Exists(metadata.ConfigSourcePath))
                    {
                        zip.CreateEntryFromFile(metadata.ConfigSourcePath, $"configfiles/{profile.FileName}.config");
                        manifestEntry.ConfigFileBundled = true;
                    }
                }

                manifest.Profiles.Add(manifestEntry);
            }

            WriteJsonEntry(zip, ManifestEntryName, manifest);
        }

        _auditLogger.Log(AuditActionType.ProfilesExported, AuditSource.MainForm, new AuditDetail
        {
            ProfileNames = manifest.Profiles.Select(p => p.FileName).ToList(),
            AffectedEntries = manifest.Profiles.Count,
            DestinationPath = zipPath
        });
    }

    public ProfileExportManifest ReadManifest(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return ReadManifest(zip);
    }

    public List<string> Import(string zipPath, IEnumerable<string> fileNamesToImport)
    {
        var notes = new List<string>();
        var importedNames = new List<string>();

        using var zip = ZipFile.OpenRead(zipPath);
        var manifest = ReadManifest(zip);

        Directory.CreateDirectory(HostsProfileList.ProfileDirectory);

        foreach (var fileName in fileNamesToImport)
        {
            var manifestEntry = manifest.Profiles.FirstOrDefault(p => p.FileName == fileName);
            var hostsZipEntry = zip.GetEntry($"profiles/{fileName}");
            if (manifestEntry == null || hostsZipEntry == null) continue;

            var destFileName = HostsProfile.NormalizeName(GenerateImportName(Path.GetFileNameWithoutExtension(fileName)));
            var destPath = Path.Combine(HostsProfileList.ProfileDirectory, destFileName);

            using (var src = hostsZipEntry.Open())
            using (var dst = File.Create(destPath))
                src.CopyTo(dst);

            var metadata = new HostsProfileMetadata { Name = destFileName };

            var sidecarEntry = zip.GetEntry($"profiles/{fileName}.json");
            if (sidecarEntry != null)
            {
                using var reader = new StreamReader(sidecarEntry.Open());
                var importedMetadata = JsonSerializer.Deserialize<HostsProfileMetadata>(reader.ReadToEnd(), JsonOptions);
                if (importedMetadata != null)
                {
                    metadata.Color = importedMetadata.Color;
                    metadata.SortOrder = importedMetadata.SortOrder;
                    metadata.Description = importedMetadata.Description;
                    // Hotkeys/config paths are never copied from the import sidecar — the
                    // exporter already stripped them; this is just defense in depth.
                }
            }

            if (manifestEntry.ConfigFileBundled)
            {
                var configZipEntry = zip.GetEntry($"configfiles/{fileName}.config");
                if (configZipEntry != null)
                {
                    var importedConfigDir = Path.Combine(HostsProfileList.ProfileDirectory, "imported-configs");
                    Directory.CreateDirectory(importedConfigDir);
                    var importedConfigPath = Path.Combine(importedConfigDir, destFileName + ".config");

                    using (var src = configZipEntry.Open())
                    using (var dst = File.Create(importedConfigPath))
                        src.CopyTo(dst);

                    // The bundled copy IS a valid local path now — safe to pre-fill.
                    // The destination (where to overwrite) is still machine-specific —
                    // left blank, the user sets it via Profile Settings on this PC.
                    metadata.ConfigSourcePath = importedConfigPath;
                    notes.Add($"\"{destFileName}\": its config file was imported to \"{importedConfigPath}\" — set where it should overwrite (Profile Settings) on this PC.");
                }
            }
            else if (manifestEntry.OriginalConfigSourcePath != null || manifestEntry.OriginalConfigDestinationPath != null)
            {
                notes.Add($"\"{destFileName}\": previously copied a config file from \"{manifestEntry.OriginalConfigSourcePath}\" to \"{manifestEntry.OriginalConfigDestinationPath}\" on the exporting machine — set this up again via Profile Settings if you still need it.");
            }

            metadata.Save(destPath);
            _profileList.Add(new HostsProfile { FilePath = destPath });
            importedNames.Add(destFileName);
        }

        _auditLogger.Log(AuditActionType.ProfilesImported, AuditSource.MainForm, new AuditDetail
        {
            ProfileNames = importedNames,
            AffectedEntries = importedNames.Count,
            SourcePath = zipPath
        });

        return notes;
    }

    private static ProfileExportManifest ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("Not a valid profile export package — manifest.json is missing.");

        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<ProfileExportManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Could not read manifest.json.");
    }

    private string GenerateImportName(string baseName)
    {
        var existingNames = _profileList
            .Select(p => Path.GetFileNameWithoutExtension(p.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName)) return baseName;

        var candidate = $"{baseName} (Imported)";
        var counter = 2;
        while (existingNames.Contains(candidate))
        {
            candidate = $"{baseName} (Imported {counter})";
            counter++;
        }
        return candidate;
    }

    private static void WriteJsonEntry<T>(ZipArchive zip, string entryName, T value)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(value, JsonOptions));
    }
}
