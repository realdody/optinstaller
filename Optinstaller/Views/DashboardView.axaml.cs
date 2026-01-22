using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Optinstaller.ViewModels;

namespace Optinstaller.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        
        // When view loads, trigger version refresh if VM is attached
        this.Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void AddGame_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            // Get the TopLevel to access StorageProvider
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                await vm.AddGameFromPath(topLevel.StorageProvider);
            }
        }
    }
}
