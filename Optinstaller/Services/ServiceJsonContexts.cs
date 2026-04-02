using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Optinstaller.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class ConfigurationServiceJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(List<GitHubRelease>))]
internal partial class VersionServiceJsonContext : JsonSerializerContext
{
}
