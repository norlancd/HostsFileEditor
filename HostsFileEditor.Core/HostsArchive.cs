using HostsFileEditor.Properties;

namespace HostsFileEditor;

public class HostsArchive
{
    private string _filePath = string.Empty;

    public HostsArchive()
    {
        FilePath = string.Empty;
    }

    public HostsArchive(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Use effective archive directory (allows test override)
        FilePath = Path.Combine(HostsArchiveList.EffectiveArchiveDirectory, name);
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _filePath = value;
        }
    }

    public string FileName => FilePath
        .Split(Path.DirectorySeparatorChar)
        .LastOrDefault() ?? string.Empty;

    public bool IsAutoBackup =>
        !string.IsNullOrEmpty(FilePath) &&
        FilePath.StartsWith(AutoBackupService.AutoBackupDirectory, StringComparison.OrdinalIgnoreCase);

    public string DisplayName
    {
        get
        {
            if (!IsAutoBackup) return FileName;

            // Parse "hosts_yyyyMMdd_HHmmss" or "hosts_yyyyMMdd_HHmmss_N"
            var name = FileName;
            if (name.StartsWith("hosts_", StringComparison.OrdinalIgnoreCase) &&
                name.Length >= 21 &&
                DateTime.TryParseExact(
                    name.Substring(6, 15),
                    "yyyyMMdd_HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var ts))
            {
                return ts.ToString("yyyy-MM-dd HH:mm:ss") + "  [host backup]";
            }

            return FileName + "  [host backup]";
        }
    }

    private HostsProfileMetadata? _metadata;
    private bool _metadataLoaded;

    public HostsProfileMetadata? Metadata
    {
        get
        {
            if (!_metadataLoaded)
            {
                _metadata = string.IsNullOrEmpty(FilePath) ? null : HostsProfileMetadata.Load(FilePath);
                _metadataLoaded = true;
            }
            return _metadata;
        }
    }

    public void ReloadMetadata()
    {
        _metadataLoaded = false;
        _metadata = null;
    }

    public static bool Validate(string filePath, out string error)
    {
        var isValid = false;

        error = string.Empty;

        try
        {
            _ = new FileInfo(filePath);
            isValid = true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (isValid)
        {
            if (Directory.Exists(HostsArchiveList.ArchiveDirectory))
            {
                if (Directory.GetFiles(HostsArchiveList.ArchiveDirectory)
                    .Select(fullFilePath => Path.GetFileName(fullFilePath))
                    .Contains(filePath))
                {
                    isValid = false;
                    error = Resources.ArchiveExists;
                }
            }
        }

        return isValid;
    }
}
