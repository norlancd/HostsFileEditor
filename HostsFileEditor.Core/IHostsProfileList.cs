using System.ComponentModel;

namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="HostsProfileList"/>'s instance surface — extracted so
/// consumers can eventually depend on this instead of the static <c>Instance</c>
/// accessor, without changing any behavior today.
/// </summary>
public interface IHostsProfileList : IList<HostsProfile>
{
    event ListChangedEventHandler ListChanged;

    void Delete(HostsProfile profile);

    void SaveMetadata(HostsProfile profile);

    void Refresh();
}
