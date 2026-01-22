using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;
using FluentAvalonia.UI.Controls;
using Optinstaller.ViewModels;
using System.Linq;

namespace Optinstaller.Views;

public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void NavigationView_SelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem item && DataContext is MainWindowViewModel vm)
        {
            switch (item.Tag)
            {
                case "Dashboard":
                    vm.CurrentPage = vm.Dashboard;
                    break;
                case "Versions":
                    vm.CurrentPage = vm.Versions;
                    break;
                case "Settings":
                    vm.CurrentPage = vm.Settings;
                    break;
            }
        }
    }
}