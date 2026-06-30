using System.Text.Json.Serialization;

namespace HostsFileEditor;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProfileExportManifest))]
[JsonSerializable(typeof(ProfileExportManifestEntry))]
[JsonSerializable(typeof(HostsProfileMetadata))]
internal partial class CoreJsonContext : JsonSerializerContext { }
