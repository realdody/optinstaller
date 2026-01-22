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
}
