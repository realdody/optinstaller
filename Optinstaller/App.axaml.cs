using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Optinstaller.ViewModels;
using Optinstaller.Views;

namespace Optinstaller;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Completes framework initialization and configures the desktop main window when running under a classic desktop lifetime.
    /// </summary>
    /// <remarks>
    /// When the application lifetime is an <c>IClassicDesktopStyleApplicationLifetime</c>, this method disables Avalonia's DataAnnotations validation plugin and sets <c>desktop.MainWindow</c> to a new <c>MainWindow</c> whose <c>DataContext</c> is a new <c>MainWindowViewModel</c>. The base implementation is invoked at the end of initialization.
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Removes all DataAnnotationsValidationPlugin instances from the global binding validators to disable Avalonia's data-annotation-based validation.
    /// </summary>
    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}