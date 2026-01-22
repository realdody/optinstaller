using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

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

    public async Task InitializeAsync()
    {
        await Dashboard.InitializeAsync();
        await Versions.InitializeAsync();
    }
}
