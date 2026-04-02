using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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
        SafeFireAndForget(RefreshVersions());
    }

    private async void SafeFireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in background task: {ex}");
        }
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
            if (Directory.Exists(path))
            {
                AddGameInternal(path);
            }
        }
    }

    private async Task RefreshVersions()
    {
        DownloadedVersions.Clear();
        var allVersions = await _versionService.GetAvailableVersionsAsync();

        foreach (var version in allVersions.Where(v => v.IsDownloaded))
        {
            DownloadedVersions.Add(version);
        }

        if (SelectedVersion != null)
        {
            SelectedVersion = DownloadedVersions.FirstOrDefault(v =>
                v.TagName.Equals(SelectedVersion.TagName, StringComparison.OrdinalIgnoreCase));
        }

        SelectedVersion ??= DownloadedVersions.FirstOrDefault();
    }

    public async Task<bool> AddGameFromPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || !Directory.Exists(rawPath))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(rawPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            normalizedPath = normalizedPath.ToLowerInvariant();
        }

        if (Games.Any(g => g.GamePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        AddGameInternal(normalizedPath);

        if (!_configService.CurrentConfig.SavedGamePaths.Contains(normalizedPath))
        {
            _configService.CurrentConfig.SavedGamePaths.Add(normalizedPath);
            await _configService.SaveAsync();
        }

        return true;
    }

    private void AddGameInternal(string path)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(trimmedPath);
        if (string.IsNullOrEmpty(dirName))
        {
            dirName = trimmedPath;
        }

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

    public InstallationWizardViewModel CreateInstallationWizard(GameInstance game)
    {
        if (!DownloadedVersions.Any())
        {
            throw new InvalidOperationException("Download an OptiScaler version before installing.");
        }

        var version = SelectedVersion ?? DownloadedVersions.First();
        return new InstallationWizardViewModel(game, DownloadedVersions, version);
    }

    public void RefreshGameInstallation(GameInstance game)
    {
        game.IsInstalled = _optiScalerService.IsInstalled(game.GamePath, out var filename, out var detectedVersion);
        game.InstalledFilename = filename;
        game.CurrentVersion = game.IsInstalled ? detectedVersion : "Not Installed";

        if (!game.IsInstalled)
        {
            game.InstalledFilename = string.Empty;
        }
    }

    public async Task UpdateOptiScaler(GameInstance game, OptiScalerVersion selectedVersion)
    {
        if (!game.IsInstalled)
        {
            throw new InvalidOperationException("The selected game does not have OptiScaler installed.");
        }

        await Task.Run(() =>
        {
            _optiScalerService.UpdateDll(game.GamePath, selectedVersion.LocalPath, game.InstalledFilename);
        });

        if (_optiScalerService.IsInstalled(game.GamePath, out _, out var newVersion))
        {
            game.CurrentVersion = newVersion;
        }
        else
        {
            game.CurrentVersion = selectedVersion.TagName;
        }
    }

    public async Task UninstallOptiScaler(GameInstance game)
    {
        if (!game.IsInstalled)
        {
            return;
        }

        await _optiScalerService.UninstallAsync(game.GamePath, game.InstalledFilename);

        game.IsInstalled = false;
        game.InstalledFilename = string.Empty;
        game.CurrentVersion = "Not Installed";
    }

    public GameConfigViewModel CreateGameConfig(GameInstance game)
    {
        if (!game.IsInstalled)
        {
            throw new InvalidOperationException("Install OptiScaler before editing its configuration.");
        }

        return new GameConfigViewModel(game.GamePath);
    }

    public async Task RemoveGame(GameInstance game)
    {
        if (game.IsInstalled)
        {
            await UninstallOptiScaler(game);
        }

        var path = game.GamePath;
        Games.Remove(game);

        if (_configService.CurrentConfig.SavedGamePaths.Contains(path))
        {
            _configService.CurrentConfig.SavedGamePaths.Remove(path);
            await _configService.SaveAsync();
        }
    }

    public void OpenGameFolder(GameInstance? game)
    {
        if (game == null || string.IsNullOrEmpty(game.GamePath))
        {
            return;
        }

        if (Directory.Exists(game.GamePath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game.GamePath,
                UseShellExecute = true
            });
        }
    }
}
