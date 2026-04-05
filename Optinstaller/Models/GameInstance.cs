using CommunityToolkit.Mvvm.ComponentModel;

namespace Optinstaller.Models;

public partial class GameInstance : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private string _executableName = string.Empty;

    [ObservableProperty]
    private bool _isInstalled;
    
    [ObservableProperty]
    private string _installedFilename = string.Empty; // e.g., dxgi.dll, winmm.dll

    [ObservableProperty]
    private string _currentVersion = "Not Installed";

    [ObservableProperty]
    private string _fsrVersion = string.Empty;

    [ObservableProperty]
    private string _antiCheatProvider = string.Empty;

    [ObservableProperty]
    private bool _isAntiCheatDetectionPending;

    [ObservableProperty]
    private bool _hasSupportedUpscalers;

    [ObservableProperty]
    private string _upscalerSummary = string.Empty;

    [ObservableProperty]
    private bool _isUpscalerDetectionPending;

    [ObservableProperty]
    private string _scanSource = string.Empty;

    [ObservableProperty]
    private string _scanSourceId = string.Empty;

    [ObservableProperty]
    private bool _isOptiPatcherInstalled;
}
