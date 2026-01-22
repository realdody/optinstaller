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
                // Ignore errors, start fresh
                CurrentConfig = new AppConfig();
            }
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            using var stream = File.Create(_configPath);
            await JsonSerializer.SerializeAsync(stream, CurrentConfig, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            // Handle save error?
        }
    }
}

public class AppConfig
{
    public List<string> SavedGamePaths { get; set; } = new();
    public string? LastSelectedVersion { get; set; }
}
