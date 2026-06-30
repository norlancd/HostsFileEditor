namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="ProfileExportImportService"/> — extracted so consumers
/// depend on this via constructor injection.
/// </summary>
public interface IProfileExportImportService
{
    /// <summary>
    /// Packages the given profiles (their .hosts content + a sanitized copy of their
    /// metadata — hotkeys and config-copy paths stripped) into a single .zip at
    /// <paramref name="zipPath"/>. Profiles named in <paramref name="bundleConfigForFileNames"/>
    /// also get their actual config file's contents embedded (opt-in — that file may hold secrets).
    /// </summary>
    void Export(string zipPath, IEnumerable<HostsProfile> profiles, IReadOnlySet<string> bundleConfigForFileNames);

    /// <summary>Reads just the manifest from a package, for the user to review before importing anything.</summary>
    ProfileExportManifest ReadManifest(string zipPath);

    /// <returns>Human-readable notes about anything the user should follow up on manually (e.g. config-copy paths that were not carried over).</returns>
    List<string> Import(string zipPath, IEnumerable<string> fileNamesToImport);
}
