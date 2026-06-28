using System.ComponentModel;

namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="HostsFile"/>'s instance surface — extracted so
/// consumers can eventually depend on this instead of the static <c>Instance</c>
/// accessor, without changing any behavior today.
/// </summary>
public interface IHostsFile : INotifyPropertyChanged
{
    HostsEntryList Entries { get; }

    int EnabledCount { get; }

    int LineCount { get; }

    bool HasUnsavedChanges { get; }

    void Import(string importFilePath, bool? removeDefaultTextOverride = null);

    void ImportFromLines(IEnumerable<string> lines);

    void SaveAsProfile(string name);

    void RestoreDefault();

    void DisableAll();

    void Save();

    bool WasModifiedExternally();

    void AcknowledgeExternalChange();

    void SaveAs(string saveFilePath);

    void Refresh(bool removeDefault = true);
}
