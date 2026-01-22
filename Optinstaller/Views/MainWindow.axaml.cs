using Avalonia.Controls;
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