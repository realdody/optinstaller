using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;

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

    public event EventHandler? RequestClose;

    public GameConfigViewModel(string gamePath)
    {
        _configPath = Path.Combine(gamePath, "OptiScaler.ini");
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (!File.Exists(_configPath)) return;

        _rawContent = File.ReadAllText(_configPath);
        
        // Simple parsing (IniParser would be better, but doing manual for simplicity/no-dep)
        EnableSpoofing = !ContainsSetting("Dxgi", "false");
        EnableOverlay = ContainsSetting("OverlayMenu", "true");
        
        var upscaler = GetSetting("Upscaler");
        UpscalerIndex = upscaler switch
        {
            "dlss" => 1,
            "fsr2" => 2,
            "xess" => 3,
            "fsr3" => 4,
            _ => 0
        };

        if (float.TryParse(GetSetting("RenderScale"), out var rs)) RenderScale = rs;
        if (float.TryParse(GetSetting("Sharpness"), out var sh)) Sharpness = sh;
    }

    private string GetSetting(string key)
    {
        foreach (var line in _rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
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

    [RelayCommand]
    private void Save()
    {
        if (!File.Exists(_configPath)) return;

        // Update raw content with new values (naive replace, ideal would be a proper parser)
        UpdateSetting("Dxgi", EnableSpoofing ? "auto" : "false");
        UpdateSetting("OverlayMenu", EnableOverlay ? "true" : "false");
        
        var upscalerVal = UpscalerIndex switch
        {
            1 => "dlss",
            2 => "fsr2",
            3 => "xess",
            4 => "fsr3",
            _ => "auto"
        };
        UpdateSetting("Upscaler", upscalerVal);

        UpdateSetting("RenderScale", RenderScale.ToString("0.0"));
        UpdateSetting("Sharpness", Sharpness.ToString("0.0"));

        File.WriteAllText(_configPath, _rawContent);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSetting(string key, string value)
    {
        var lines = _rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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
            // Append to [Display] or general? Just append to end
            _rawContent += $"{Environment.NewLine}{key}={value}";
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        try
        {
            new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(_configPath)
                {
                    UseShellExecute = true
                }
            }.Start();
        }
        catch { }
    }
}
