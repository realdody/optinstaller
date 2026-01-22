using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Optinstaller.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "config.json";
    private readonly string _configPath;

    public AppConfig CurrentConfig { get; private set; } = new();

    public ConfigurationService()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
    }

    /// <summary>
    /// Loads the application configuration from the configured file into <see cref="CurrentConfig"/> if the file exists.
    /// </summary>
    /// <remarks>
    /// If the file does not exist, <see cref="CurrentConfig"/> is left unchanged. If deserialization fails or any error occurs while reading, <see cref="CurrentConfig"/> is reset to a new <see cref="AppConfig"/> instance.
    /// </remarks>
    /// <returns>A <see cref="Task"/> that completes when the load operation has finished.</returns>
    public async Task LoadAsync()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                using var stream = File.OpenRead(_configPath);
                CurrentConfig = await JsonSerializer.DeserializeAsync<AppConfig>(stream) ?? new AppConfig();
            }
            catch
            {
                CurrentConfig = new AppConfig();
            }
        }
    }

    /// <summary>
    /// Writes the current configuration to the configuration file, creating or overwriting the file at the configured path.
    /// </summary>
    /// <remarks>
    /// Any errors encountered while writing are swallowed; the method completes without throwing on failure.
    /// </remarks>
    /// <returns>A Task that completes when the write attempt has finished (successfully or not).</returns>
    public async Task SaveAsync()
    {
        try
        {
            using var stream = File.Create(_configPath);
            await JsonSerializer.SerializeAsync(stream, CurrentConfig, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
        }
    }
}

public class AppConfig
{
    public List<string> SavedGamePaths { get; set; } = new();
    public string? LastSelectedVersion { get; set; }
}