using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Optinstaller.Messages;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IRecipient<VersionsChangedMessage>
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
    private bool _enableSpoofing = true;

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

        WeakReferenceMessenger.Default.Register(this);
    }
    
    public void Receive(VersionsChangedMessage message)
    {
        _ = RefreshVersions();
    }
    
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

    public async Task AddGameFromPath(IStorageProvider storageProvider)
    {
        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Game Directory",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            var rawPath = result[0].Path.LocalPath;
            var normalizedPath = System.IO.Path.GetFullPath(rawPath)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

            if (OperatingSystem.IsWindows())
            {
                normalizedPath = normalizedPath.ToLowerInvariant();
            }

            if (Games.Any(g => g.GamePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))) return;

            AddGameInternal(normalizedPath);
            
            if (!_configService.CurrentConfig.SavedGamePaths.Contains(normalizedPath))
            {
                _configService.CurrentConfig.SavedGamePaths.Add(normalizedPath);
                await _configService.SaveAsync();
            }
        }
    }

    private void AddGameInternal(string path)
    {
        var trimmedPath = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var dirName = System.IO.Path.GetFileName(trimmedPath);
        if (string.IsNullOrEmpty(dirName)) dirName = trimmedPath;

        var isInstalled = _optiScalerService.IsInstalled(path, out var installedFilename, out var detectedVersion);
        
        var game = new GameInstance
        {
            Name = dirName,
            GamePath = path,
            IsInstalled = isInstalled,
            InstalledFilename = installedFilename,
            CurrentVersion = isInstalled ? detectedVersion : "Not Installed"
        };
        Games.Add(game);
    }

    [RelayCommand]
    private async Task InstallOptiScaler(GameInstance? game)
    {
        if (game == null) return;
        
        if (!DownloadedVersions.Any())
        {
            var errorDialog = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "No Versions Available",
                Content = "Please download an OptiScaler version from the Versions tab before installing.",
                CloseButtonText = "OK"
            };
            await errorDialog.ShowAsync();
            return;
        }

        var version = SelectedVersion ?? DownloadedVersions.First();

        var wizardVm = new InstallationWizardViewModel(game, DownloadedVersions, version);
        
        var window = new Views.InstallationWizardWindow 
        { 
            DataContext = wizardVm 
        };

        wizardVm.RequestClose += (s, e) => window.Close();
        
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
             await window.ShowDialog(desktop.MainWindow);
        }

        game.IsInstalled = _optiScalerService.IsInstalled(game.GamePath, out var filename, out var detectedVersion);
        game.InstalledFilename = filename;
        
        if (game.IsInstalled)
        {
             game.CurrentVersion = detectedVersion;
        }
        else
        {
             game.CurrentVersion = "Not Installed";
             game.InstalledFilename = string.Empty;
        }
    }

    [RelayCommand]
    private async Task UpdateOptiScaler(GameInstance? game)
    {
        if (game == null || !game.IsInstalled) return;

        if (!DownloadedVersions.Any())
        {
            var errorDialog = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "No Versions Available",
                Content = "Please download an OptiScaler version from the Versions tab before updating.",
                CloseButtonText = "OK"
            };
            await errorDialog.ShowAsync();
            return;
        }

        // Show a dialog to select the version
        var comboBox = new ComboBox
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            DisplayMemberBinding = new Avalonia.Data.Binding("TagName"),
            ItemsSource = DownloadedVersions,
            SelectedItem = SelectedVersion ?? DownloadedVersions.First()
        };

        var dialog = new FluentAvalonia.UI.Controls.ContentDialog
        {
            Title = "Select Version to Update",
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Choose the version to update to:" },
                    comboBox
                }
            }
        };

        var result = await dialog.ShowAsync();
        if (result != FluentAvalonia.UI.Controls.ContentDialogResult.Primary) return;

        var selectedVersion = comboBox.SelectedItem as OptiScalerVersion;
        if (selectedVersion == null) return;

        try
        {
            // Use UpdateDll to replace the file without touching config
            await Task.Run(() => 
            {
                 _optiScalerService.UpdateDll(game.GamePath, selectedVersion.LocalPath, game.InstalledFilename);
            });

            // Re-detect version from disk to be sure
            if (_optiScalerService.IsInstalled(game.GamePath, out _, out var newVersion))
            {
                game.CurrentVersion = newVersion;
            }
            else
            {
                // Fallback if detection fails for some reason
                game.CurrentVersion = selectedVersion.TagName;
            }
            
            var successDialog = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "Update Complete",
                Content = $"OptiScaler updated to {selectedVersion.TagName}.",
                CloseButtonText = "OK"
            };
            await successDialog.ShowAsync();
        }
        catch (Exception ex)
        {
             var errorDialog = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "Update Error",
                Content = $"Failed to update: {ex.Message}",
                CloseButtonText = "OK"
            };
            await errorDialog.ShowAsync();
        }
    }

    [RelayCommand]
    private async Task UninstallOptiScaler(GameInstance? game)
    {
        if (game == null || !game.IsInstalled) return;
        await PerformUninstall(game);
    }

    private async Task<bool> PerformUninstall(GameInstance game)
    {
        var dialog = new FluentAvalonia.UI.Controls.ContentDialog
        {
            Title = "Confirm Uninstall",
            Content = $"Are you sure you want to uninstall OptiScaler from {game.Name}?",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel"
        };

        if (await dialog.ShowAsync() != FluentAvalonia.UI.Controls.ContentDialogResult.Primary)
            return false;

        try
        {
            await _optiScalerService.UninstallAsync(game.GamePath, game.InstalledFilename);

            game.IsInstalled = false;
            game.InstalledFilename = string.Empty;
            game.CurrentVersion = "Not Installed";
            return true;
        }
        catch (Exception ex)
        {
            var errorDialog = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "Uninstall Error",
                Content = $"Failed to uninstall: {ex.Message}",
                CloseButtonText = "OK"
            };
            await errorDialog.ShowAsync();
            return false;
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
        
        if (game.IsInstalled)
        {
            var uninstalled = await PerformUninstall(game);
            if (!uninstalled) return;
        }

        var path = game.GamePath;
        Games.Remove(game);
        
        if (_configService.CurrentConfig.SavedGamePaths.Contains(path))
        {
            _configService.CurrentConfig.SavedGamePaths.Remove(path);
            await _configService.SaveAsync();
        }
    }

    [RelayCommand]
    private void OpenGameFolder(GameInstance? game)
    {
        if (game == null || string.IsNullOrEmpty(game.GamePath)) return;
        
        if (System.IO.Directory.Exists(game.GamePath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game.GamePath,
                UseShellExecute = true
            });
        }
    }
}
