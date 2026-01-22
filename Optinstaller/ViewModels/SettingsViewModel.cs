using CommunityToolkit.Mvvm.ComponentModel;

namespace Optinstaller.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _optiScalerDownloadUrl = "https://github.com/OptiScaler/OptiScaler/releases/latest";
    
    [ObservableProperty]
    private bool _enableOverlay = true;
}
