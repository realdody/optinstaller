using CommunityToolkit.Mvvm.ComponentModel;

namespace Optinstaller.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    public DashboardViewModel Dashboard { get; } = new();
    public VersionManagerViewModel Versions { get; } = new();
    public SettingsViewModel Settings { get; } = new();

    public MainWindowViewModel()
    {
        _currentPage = Dashboard;
    }
}
