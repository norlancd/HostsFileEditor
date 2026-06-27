using HostsFileEditor.Properties;

namespace HostsFileEditor;

public class HostsProfile
{
    private string _filePath = string.Empty;

    public HostsProfile()
    {
        FilePath = string.Empty;
    }

    public HostsProfile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Use effective profile directory (allows test override)
        FilePath = Path.Combine(HostsProfileList.EffectiveProfileDirectory, NormalizeName(name));
    }

    public const string ProfileExtension = ".hosts";

    public static string NormalizeName(string name) =>
        name.EndsWith(ProfileExtension, StringComparison.OrdinalIgnoreCase)
            ? name
            : name + ProfileExtension;

    public string FilePath
    {
        get => _filePath;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _filePath = value;
        }
    }

    // HostsProfileList.Refresh() recreates HostsProfile instances for every file on
    // disk. Reference equality would silently break "is this the active profile"
    // checks across a refresh, so equality is defined by FilePath — the durable
    // identity of a profile.
    public override bool Equals(object? obj) =>
        obj is HostsProfile other &&
        string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        FilePath.ToUpperInvariant().GetHashCode();

    public static bool operator ==(HostsProfile? left, HostsProfile? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(HostsProfile? left, HostsProfile? right) => !(left == right);

    public string FileName => FilePath
        .Split(Path.DirectorySeparatorChar)
        .LastOrDefault() ?? string.Empty;

    /// <summary>
    /// True for the reserved "Default" profile — always present, can't be deleted,
    /// shown pinned/italic at the top of the Profiles menu instead of in the regular list.
    /// </summary>
    public bool IsDefault =>
        string.Equals(FileName, HostsProfileList.DefaultProfileFileName, StringComparison.OrdinalIgnoreCase);

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

        filePath = NormalizeName(filePath);

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
            if (Directory.Exists(HostsProfileList.ProfileDirectory))
            {
                if (Directory.GetFiles(HostsProfileList.ProfileDirectory)
                    .Select(fullFilePath => Path.GetFileName(fullFilePath))
                    .Contains(filePath, StringComparer.OrdinalIgnoreCase))
                {
                    isValid = false;
                    error = Resources.ProfileExists;
                }
            }
        }

        return isValid;
    }
}
