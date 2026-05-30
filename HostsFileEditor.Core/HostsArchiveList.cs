using HostsFileEditor.Extensions;
using HostsFileEditor.Utilities;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace HostsFileEditor;

public class HostsArchiveList : BindingList<HostsArchive>
{
    public static readonly string ArchiveDirectory =
        Path.Combine(HostsFile.DefaultHostFileDirectory, "archive");

    // Test hook to override archive directory for safe unit testing
    internal static string? TestArchiveDirectoryOverride { get; set; }

    internal static string EffectiveArchiveDirectory => TestArchiveDirectoryOverride ?? ArchiveDirectory;

    private static readonly Lazy<HostsArchiveList> _instance =
        new(() => new HostsArchiveList());

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingList used only for simple collection change notifications; PropertyDescriptor reflection not exercised.")]
    private HostsArchiveList()
    {
        Refresh();
    }

    public static HostsArchiveList Instance => _instance.Value;

    public void Delete(HostsArchive archive)
    {
        using (FileEx.DisableAttributes(archive.FilePath, FileAttributes.ReadOnly))
        {
            File.Delete(archive.FilePath);
        }

        // Remove sidecar metadata if it exists
        archive.Metadata?.Delete(archive.FilePath);

        Remove(archive);
    }

    public void SaveMetadata(HostsArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Metadata?.Save(archive.FilePath);
        archive.ReloadMetadata();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingList used only for simple collection change notifications; PropertyDescriptor reflection not exercised.")]
    public void Refresh()
    {
        this.BatchUpdate(() =>
        {
            Clear();

            if (Directory.Exists(EffectiveArchiveDirectory))
            {
                // User-created archives — immediate children only, skip __autobak and .json sidecars
                var autoBackupDir = AutoBackupService.AutoBackupDirectory;
                var files = Directory.GetFiles(EffectiveArchiveDirectory)
                    .Where(f =>
                        !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        !f.StartsWith(autoBackupDir, StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(f).StartsWith("__rollback_", StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                    Add(new HostsArchive { FilePath = file });

                // Auto-backups — enumerated last so they appear below user archives
                if (Directory.Exists(autoBackupDir))
                {
                    var backups = Directory.GetFiles(autoBackupDir)
                        .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase);

                    foreach (var file in backups)
                        Add(new HostsArchive { FilePath = file });
                }
            }
        });
    }
}
