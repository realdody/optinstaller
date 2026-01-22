using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly OptiScalerService _optiScalerService;
    private readonly VersionService _versionService;
    private readonly ConfigurationService _configService;

    [ObservableProperty]
    private ObservableCollection<GameInstance> _games = new();

    [ObservableProperty]
    private GameInstance? _selectedGame;

    [ObservableProperty]
    private ObservableCollection<OptiScalerVersion> _downloadedVersions = new();

    [ObservableProperty]
    private OptiScalerVersion? _selectedVersion;

    [ObservableProperty]
    private string _targetFilename = "dxgi.dll";

    [ObservableProperty]
    private bool _enableSpoofing = true; // Default to true (Nvidia behavior)

    public List<string> TargetFilenames { get; } = new()
    {
        "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll",
        "d3d12.dll", "wininet.dll", "winhttp.dll", "OptiScaler.asi"
    };

    public DashboardViewModel()
    {
        _optiScalerService = new OptiScalerService();
        _versionService = new VersionService();
        _configService = new ConfigurationService();
    }
    
    // Quick hack to initialize async data
    public async Task InitializeAsync()
    {
        await _configService.LoadAsync();
        await RefreshVersions();
        LoadGames();
    }

    private void LoadGames()
    {
        Games.Clear();
        foreach (var path in _configService.CurrentConfig.SavedGamePaths)
        {
             if (System.IO.Directory.Exists(path))
             {
                 AddGameInternal(path);
             }
        }
    }

    private async Task RefreshVersions()
    {
        DownloadedVersions.Clear();
        // This now includes locally found versions even if GitHub is unreachable
        var allVersions = await _versionService.GetAvailableVersionsAsync();
        
        foreach (var v in allVersions.Where(v => v.IsDownloaded))
        {
            DownloadedVersions.Add(v);
        }
        
        if (DownloadedVersions.Any())
            SelectedVersion = DownloadedVersions.First();
    }

    // Method to be called from View code-behind with the storage provider
    public async Task AddGameFromPath(IStorageProvider storageProvider)
    {
        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Game Directory",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            var path = result[0].Path.LocalPath;
            if (Games.Any(g => g.GamePath == path)) return;

            AddGameInternal(path);
            
            // Save config
            if (!_configService.CurrentConfig.SavedGamePaths.Contains(path))
            {
                _configService.CurrentConfig.SavedGamePaths.Add(path);
                await _configService.SaveAsync();
            }
        }
    }

    private void AddGameInternal(string path)
    {
        var dirName = System.IO.Path.GetFileName(path);
        var game = new GameInstance
        {
            Name = dirName,
            GamePath = path,
            IsInstalled = _optiScalerService.IsInstalled(path, out var installedFilename),
            InstalledFilename = installedFilename
        };
        Games.Add(game);
    }

    [RelayCommand]
    private async Task InstallOptiScaler(GameInstance? game)
    {
        if (game == null) return;
        
        // Ensure a version is selected. If multiple, maybe ask? For now, default to newest downloaded.
        // We need to ensure we have versions.
        if (!DownloadedVersions.Any())
        {
            // TODO: Show error "Please download a version first"
            return;
        }

        var version = SelectedVersion ?? DownloadedVersions.First();

        // Launch Wizard
        var wizardVm = new InstallationWizardViewModel(game, version);
        
        var dialog = new FluentAvalonia.UI.Controls.ContentDialog
        {
            Title = "Install OptiScaler",
            Content = new Views.InstallationWizardView { DataContext = wizardVm },
            PrimaryButtonText = null, // Managed by view
            SecondaryButtonText = null,
            CloseButtonText = null
        };

        // Handle close request from VM
        wizardVm.RequestClose += (s, e) => dialog.Hide();

        await dialog.ShowAsync();

        // Refresh game status after wizard closes
        game.IsInstalled = _optiScalerService.IsInstalled(game.GamePath, out var filename);
        game.InstalledFilename = filename;
        if (game.IsInstalled)
        {
             // We don't track version exactly per game yet, but we could update if we did
             game.CurrentVersion = version.TagName;
        }
    }

    [RelayCommand]
    private async Task UninstallOptiScaler(GameInstance? game)
    {
        if (game == null || !game.IsInstalled) return;

        var dialog = new FluentAvalonia.UI.Controls.ContentDialog
        {
            Title = "Confirm Uninstall",
            Content = $"Are you sure you want to uninstall OptiScaler from {game.Name}?",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel"
        };

        if (await dialog.ShowAsync() != FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
            return;

        try
        {
            await _optiScalerService.UninstallAsync(game.GamePath, game.InstalledFilename);

            game.IsInstalled = false;
            game.InstalledFilename = string.Empty;
            game.CurrentVersion = "Not Installed";
        }
        catch (Exception ex)
        {
            // Simple error reporting
            var errorDialog = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "Uninstall Error",
                Content = $"Failed to uninstall: {ex.Message}",
                CloseButtonText = "OK"
            };
            await errorDialog.ShowAsync();
        }
    }
    [RelayCommand]
    private async Task ConfigureGame(GameInstance? game)
    {
        if (game == null || !game.IsInstalled) return;

        var vm = new GameConfigViewModel(game.GamePath);
        var dialog = new FluentAvalonia.UI.Controls.ContentDialog
        {
            Title = "Configuration",
            Content = new Views.GameConfigView { DataContext = vm },
            PrimaryButtonText = null,
            CloseButtonText = "Cancel",
            DefaultButton = FluentAvalonia.UI.Controls.ContentDialogButton.Close
        };

        vm.RequestClose += (s, e) => dialog.Hide();

        await dialog.ShowAsync();
    }
    
    [RelayCommand]
    private async Task RemoveGame(GameInstance? game)
    {
        if (game == null) return;
        
        var path = game.GamePath;
        Games.Remove(game);
        
        if (_configService.CurrentConfig.SavedGamePaths.Contains(path))
        {
            _configService.CurrentConfig.SavedGamePaths.Remove(path);
            await _configService.SaveAsync();
        }
    }
}
