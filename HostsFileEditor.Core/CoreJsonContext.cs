using System.Text.Json.Serialization;

namespace HostsFileEditor;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProfileExportManifest))]
[JsonSerializable(typeof(ProfileExportManifestEntry))]
[JsonSerializable(typeof(HostsProfileMetadata))]
[JsonSerializable(typeof(FileReplacement))]
[JsonSerializable(typeof(List<FileReplacement>))]
[JsonSerializable(typeof(ProfileCommand))]
[JsonSerializable(typeof(List<ProfileCommand>))]
[JsonSerializable(typeof(ProfileCommandTiming))]
internal partial class CoreJsonContext : JsonSerializerContext { }
