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
    }

    private async void AddGame_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider != null)
            {
                await vm.AddGameFromPath(topLevel.StorageProvider);
            }
        }
    }
}
