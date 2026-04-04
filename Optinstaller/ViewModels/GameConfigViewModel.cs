using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Optinstaller.ViewModels;

public partial class GameConfigViewModel : ViewModelBase
{
    private readonly string _configPath;
    
    [ObservableProperty] private bool _enableSpoofing;
    [ObservableProperty] private bool _enableOverlay;
    [ObservableProperty] private int _upscalerIndex;
    [ObservableProperty] private float _renderScale = 1.0f;
    [ObservableProperty] private float _sharpness = 0.0f;
    
    private string _rawContent = "";

    public string[] Upscalers { get; } = { "Auto", "DLSS", "FSR2", "XeSS", "FSR3" };

    public string GamePath { get; }

    public string ConfigPath => _configPath;

    public bool HasConfigFile => File.Exists(_configPath);

    public event EventHandler? RequestClose;

    public GameConfigViewModel(string gamePath)
    {
        GamePath = gamePath;
        _configPath = Path.Combine(gamePath, "OptiScaler.ini");
        LoadConfig();
    }

    private void LoadConfig()
    {
        _rawContent = HasConfigFile ? File.ReadAllText(_configPath) : string.Empty;
        
        // Simple parsing (IniParser would be better, but doing manual for simplicity/no-dep)
        EnableSpoofing = !ContainsSetting("Spoofing", "Dxgi", "false");
        EnableOverlay = ContainsSetting("Menu", "OverlayMenu", "true");
        
        var upscaler = GetSetting("Upscalers", "Dx12Upscaler");
        UpscalerIndex = upscaler switch
        {
            "dlss" => 1,
            "fsr22" => 2,
            "xess" => 3,
            "fsr31" => 4,
            _ => 0
        };

        if (TryGetFloatSetting("OutputScaling", "Multiplier", out var rs)) RenderScale = rs;
        if (TryGetFloatSetting("Sharpness", "Sharpness", out var sh)) Sharpness = sh;
    }

    public void Reload()
    {
        LoadConfig();
    }

    public string GetSetting(string section, string key, string defaultValue = "auto")
    {
        var value = GetSettingInternal(section, key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    public void SetSetting(string section, string key, string value)
    {
        UpdateSetting(section, key, string.IsNullOrWhiteSpace(value) ? "auto" : value);
    }

    public void SaveChanges()
    {
        File.WriteAllText(_configPath, _rawContent);
    }

    private string GetSetting(string key)
    {
        foreach (var line in SplitLines())
        {
            var trim = line.Trim();
            if (trim.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return trim.Substring(key.Length + 1).Trim();
            }
        }
        return string.Empty;
    }

    private bool ContainsSetting(string key, string value)
    {
        var set = GetSetting(key);
        return set.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsSetting(string section, string key, string value)
    {
        var set = GetSetting(section, key);
        return set.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetFloatSetting(string section, string key, out float value)
    {
        return float.TryParse(GetSetting(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    [RelayCommand]
    public void Save()
    {
        // Update raw content with new values (naive replace, ideal would be a proper parser)
        UpdateSetting("Spoofing", "Dxgi", EnableSpoofing ? "auto" : "false");
        UpdateSetting("Menu", "OverlayMenu", EnableOverlay ? "true" : "false");
        
        var upscalerVal = UpscalerIndex switch
        {
            1 => "dlss",
            2 => "fsr22",
            3 => "xess",
            4 => "fsr31",
            _ => "auto"
        };
        UpdateSetting("Upscalers", "Dx12Upscaler", upscalerVal);

        UpdateSetting("OutputScaling", "Multiplier", RenderScale.ToString("0.0", CultureInfo.InvariantCulture));
        UpdateSetting("Sharpness", "Sharpness", Sharpness.ToString("0.0", CultureInfo.InvariantCulture));

        SaveChanges();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSetting(string key, string value)
    {
        var lines = SplitLines();
        bool found = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var trim = lines[i].Trim();
            if (trim.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                found = true;
                break;
            }
        }

        if (found)
        {
            _rawContent = string.Join(Environment.NewLine, lines);
        }
        else
        {
            _rawContent = string.IsNullOrEmpty(_rawContent)
                ? $"{key}={value}"
                : _rawContent + $"{Environment.NewLine}{key}={value}";
        }
    }

    private void UpdateSetting(string section, string key, string value)
    {
        var lines = new System.Collections.Generic.List<string>(SplitLines());
        var sectionHeader = $"[{section}]";
        var headerIndex = -1;
        var sectionEnd = lines.Count;

        for (var i = 0; i < lines.Count; i++)
        {
            var trim = lines[i].Trim();
            if (!IsSectionHeader(trim))
            {
                continue;
            }

            if (headerIndex >= 0)
            {
                sectionEnd = i;
                break;
            }

            if (trim.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                headerIndex = i;
            }
        }

        if (headerIndex >= 0)
        {
            for (var i = headerIndex + 1; i < sectionEnd; i++)
            {
                var trim = lines[i].Trim();
                if (trim.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{key}={value}";
                    _rawContent = string.Join(Environment.NewLine, lines);
                    return;
                }
            }

            lines.Insert(sectionEnd, $"{key}={value}");
            _rawContent = string.Join(Environment.NewLine, lines);
            return;
        }

        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }

        lines.Add(sectionHeader);
        lines.Add($"{key}={value}");
        _rawContent = string.Join(Environment.NewLine, lines);
    }

    private string? GetSettingInternal(string section, string key)
    {
        var inRequestedSection = false;

        foreach (var line in SplitLines())
        {
            var trim = line.Trim();
            if (IsSectionHeader(trim))
            {
                inRequestedSection = trim.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inRequestedSection)
            {
                continue;
            }

            if (trim.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return trim.Substring(key.Length + 1).Trim();
            }
        }

        return null;
    }

    private string[] SplitLines()
    {
        return _rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }

    private static bool IsSectionHeader(string value)
    {
        return value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal);
    }

    [RelayCommand]
    public void OpenFile()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException($"The OptiScaler config file was not found: {_configPath}", _configPath);
        }

        try
        {
            Process.Start(new ProcessStartInfo(_configPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to open the OptiScaler config file '{_configPath}'.", ex);
        }
    }
}
