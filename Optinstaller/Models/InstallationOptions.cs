using System;

namespace Optinstaller.Models;

public class InstallationOptions
{
    public string GamePath { get; set; } = string.Empty;
    public string VersionPath { get; set; } = string.Empty;
    public string TargetFilename { get; set; } = "dxgi.dll";
    public bool EnableSpoofing { get; set; }
    public bool UseOptiPatcher { get; set; }
    public bool CreateUninstaller { get; set; } = true;
    public bool IsNvidia { get; set; } // Detected state
    public bool IsWine { get; set; }   // Detected state
}
