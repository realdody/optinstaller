using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Optinstaller.ViewModels;

namespace Optinstaller.Views;

public partial class DashboardView : UserControl
{
    /// <summary>
    /// Initializes a new instance of <see cref="DashboardView"/>, constructs the control's UI, and registers a Loaded event handler to perform view initialization when the control becomes loaded.
    /// </summary>
    public DashboardView()
    {
        InitializeComponent();
        
        this.Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    /// <summary>
    /// Handles the Add Game button click by asking the view model to add a game from the application's top-level storage provider when available.
    /// </summary>
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