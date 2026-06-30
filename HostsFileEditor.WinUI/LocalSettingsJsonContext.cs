using System.Text.Json.Serialization;

namespace HostsFileEditor;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class LocalSettingsJsonContext : JsonSerializerContext { }
