using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Optinstaller.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(SavedGameEntry))]
[JsonSerializable(typeof(List<SavedGameEntry>))]
internal partial class ConfigurationServiceJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(List<GitHubRelease>))]
internal partial class VersionServiceJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(List<AntiCheatCatalogEntry>))]
internal partial class AntiCheatDetectionJsonContext : JsonSerializerContext
{
}
