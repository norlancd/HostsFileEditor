using HostsFileEditor.Extensions;
using HostsFileEditor.Properties;
using HostsFileEditor.Utilities;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace HostsFileEditor;

public class HostsProfileList : BindingList<HostsProfile>, IHostsProfileList
{
    public static readonly string ProfileDirectory =
        Path.Combine(HostsFile.DefaultHostFileDirectory, "profiles");

    // "Default" is a real, persisted profile like any other — this is its reserved
    // file name, so Raw Edit/Profile Settings/Activate Temporarily all work on it
    // for free via the same code paths as user-created profiles.
    public const string DefaultProfileFileName = "Default" + HostsProfile.ProfileExtension;

    // Older versions stored profiles under "archive\" — migrated once, automatically,
    // on first access below so existing installs don't lose their saved profiles.
    private static readonly string LegacyArchiveDirectory =
        Path.Combine(HostsFile.DefaultHostFileDirectory, "archive");

    // Test hook to override profile directory for safe unit testing
    internal static string? TestProfileDirectoryOverride { get; set; }

    internal static string EffectiveProfileDirectory => TestProfileDirectoryOverride ?? ProfileDirectory;

    private static readonly Lazy<HostsProfileList> _instance =
        new(() => new HostsProfileList());

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingList used only for simple collection change notifications; PropertyDescriptor reflection not exercised.")]
    private HostsProfileList()
    {
        MigrateLegacyArchiveDirectoryIfNeeded();
        EnsureDefaultProfileExists();
        Refresh();
    }

    public static HostsProfileList Instance => _instance.Value;

    public void Delete(HostsProfile profile)
    {
        using (FileEx.DisableAttributes(profile.FilePath, FileAttributes.ReadOnly))
        {
            File.Delete(profile.FilePath);
        }

        // Remove sidecar metadata if it exists
        profile.Metadata?.Delete(profile.FilePath);

        Remove(profile);
    }

    public void SaveMetadata(HostsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Metadata?.Save(profile.FilePath);
        profile.ReloadMetadata();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "BindingList used only for simple collection change notifications; PropertyDescriptor reflection not exercised.")]
    public void Refresh()
    {
        this.BatchUpdate(() =>
        {
            Clear();

            if (Directory.Exists(EffectiveProfileDirectory))
            {
                // User-created profiles — skip .json sidecars and rollback-timer snapshots
                var files = Directory.GetFiles(EffectiveProfileDirectory)
                    .Where(f =>
                        !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(f).StartsWith("__rollback_", StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                    Add(new HostsProfile { FilePath = file });
            }
        });
    }

    private static void MigrateLegacyArchiveDirectoryIfNeeded()
    {
        if (TestProfileDirectoryOverride != null) return; // never migrate during tests
        if (Directory.Exists(ProfileDirectory)) return; // already migrated (or fresh install)
        if (!Directory.Exists(LegacyArchiveDirectory)) return; // nothing to migrate

        try
        {
            Directory.Move(LegacyArchiveDirectory, ProfileDirectory);
        }
        catch (IOException)
        {
            // Leave the old directory in place if the move fails for any reason (e.g. a
            // file in use) — Refresh() will simply show no profiles rather than crash on startup.
        }
    }

    private static void EnsureDefaultProfileExists()
    {
        if (TestProfileDirectoryOverride != null) return; // tests manage their own fixtures

        try
        {
            Directory.CreateDirectory(ProfileDirectory);
            var path = Path.Combine(ProfileDirectory, DefaultProfileFileName);
            if (!File.Exists(path))
                File.WriteAllText(path, Resources.hosts);
        }
        catch (IOException)
        {
            // Best-effort — Default just won't have its own Raw Edit/Settings backing
            // until this succeeds on a later start.
        }
    }
}
