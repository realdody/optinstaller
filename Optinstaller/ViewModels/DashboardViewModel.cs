using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Optinstaller.Messages;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IRecipient<VersionsChangedMessage>
{
    private static readonly string[] GenericGameDirectoryNames =
    {
        "Binaries", "Binary", "Bin", "Win64", "Win32", "x64", "x86", "Release", "Debug"
    };

    private readonly OptiScalerService _optiScalerService;
    private readonly VersionService _versionService;
    private readonly ConfigurationService _configService;
    private readonly SemaphoreSlim _versionRefreshLock = new(1, 1);

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
        await NormalizeSavedGamePathsAsync();
        await RefreshVersions();
        LoadGames();
    }

    private void LoadGames()
    {
        Games.Clear();
        foreach (var savedGame in GetSavedGames())
        {
            if (Directory.Exists(savedGame.GamePath))
            {
                AddGameInternal(savedGame.GamePath, savedGame.ExecutablePath);
            }
        }
    }

    private async Task RefreshVersions()
    {
        await _versionRefreshLock.WaitAsync();
        try
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
        finally
        {
            _versionRefreshLock.Release();
        }
    }

    public async Task<GameInstance?> AddGameFromExecutable(string rawExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(rawExecutablePath))
        {
            return null;
        }

        var selectedPath = Path.GetFullPath(rawExecutablePath);
        if (!File.Exists(selectedPath) ||
            !string.Equals(Path.GetExtension(selectedPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a valid .exe file.");
        }

        var executablePath = ResolveSelectedExecutablePath(selectedPath);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine a usable game executable from the selected file.");
        }

        var gamePath = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            throw new InvalidOperationException("Could not determine the game folder from the selected executable.");
        }

        var normalizedPath = NormalizeGamePath(gamePath);

        if (Games.Any(g => g.GamePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var game = AddGameInternal(normalizedPath, executablePath);

        var savedGames = GetSavedGames();
        if (!savedGames.Any(saved =>
                NormalizeGamePath(saved.GamePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            savedGames.Add(new SavedGameEntry
            {
                GamePath = normalizedPath,
                ExecutablePath = NormalizeExecutablePath(executablePath, normalizedPath),
            });
            SaveSavedGames(savedGames);
            await _configService.SaveAsync();
        }

        return game;
    }

    private GameInstance AddGameInternal(string path, string? executablePath = null)
    {
        var normalizedPath = NormalizeGamePath(path);
        var resolvedExecutablePath = ResolveExecutablePath(normalizedPath, executablePath);
        var displayName = ResolveGameDisplayName(normalizedPath, resolvedExecutablePath);

        var isInstalled = _optiScalerService.IsInstalled(normalizedPath, out var installedFilename, out var detectedVersion, out var fsrVersion, out var isOptiPatcherInstalled);

        var game = new GameInstance
        {
            Name = displayName,
            GamePath = normalizedPath,
            ExecutableName = resolvedExecutablePath == null ? string.Empty : Path.GetFileName(resolvedExecutablePath),
        };

        ApplyInstallationState(game, isInstalled, installedFilename, detectedVersion, fsrVersion, isOptiPatcherInstalled);

        Games.Add(game);
        return game;
    }

    private static string ResolveGameDisplayName(string gamePath, string? executablePath)
    {
        var metadataName = TryGetExecutableMetadataName(executablePath);
        if (!string.IsNullOrWhiteSpace(metadataName))
        {
            return metadataName;
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var cleanedExecutableName = CleanGameName(Path.GetFileNameWithoutExtension(executablePath));
            if (!string.IsNullOrWhiteSpace(cleanedExecutableName))
            {
                return cleanedExecutableName;
            }
        }

        var directoryName = CleanGameName(GetPreferredGameDirectoryName(gamePath));
        return string.IsNullOrWhiteSpace(directoryName) ? gamePath : directoryName;
    }

    private static string? ResolveExecutablePath(string gamePath, string? explicitExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutablePath) && File.Exists(explicitExecutablePath))
        {
            return Path.GetFullPath(explicitExecutablePath);
        }

        if (!Directory.Exists(gamePath))
        {
            return null;
        }

        try
        {
            var executablePaths = Directory.EnumerateFiles(gamePath, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            if (executablePaths.Count == 0)
            {
                return null;
            }

            var preferredDirectoryName = CleanGameName(GetPreferredGameDirectoryName(gamePath));
            if (!string.IsNullOrWhiteSpace(preferredDirectoryName))
            {
                var exactMatch = executablePaths
                    .Where(path => CleanGameName(Path.GetFileNameWithoutExtension(path)).Equals(preferredDirectoryName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(GetExecutablePreference)
                    .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (exactMatch != null)
                {
                    return exactMatch;
                }

                var prefixMatch = executablePaths
                    .Where(path => CleanGameName(Path.GetFileNameWithoutExtension(path)).StartsWith(preferredDirectoryName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(GetExecutablePreference)
                    .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (prefixMatch != null)
                {
                    return prefixMatch;
                }
            }

            return executablePaths
                .OrderBy(GetExecutablePreference)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSelectedExecutablePath(string executablePath)
    {
        if (!File.Exists(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory) || !Directory.Exists(executableDirectory))
        {
            return null;
        }

        if (!Directory.Exists(Path.Combine(executableDirectory, "Engine")))
        {
            return executablePath;
        }

        return TryResolveUnrealExecutableFromRoot(executableDirectory) ?? executablePath;
    }

    private static string? TryResolveUnrealExecutableFromRoot(string gameRootPath)
    {
        try
        {
            string? bestCandidate = null;
            var bestPreference = int.MaxValue;

            foreach (var codeNameDirectory in Directory.EnumerateDirectories(gameRootPath))
            {
                var codeName = Path.GetFileName(codeNameDirectory);
                if (string.Equals(codeName, "Engine", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var binariesDirectory = Path.Combine(codeNameDirectory, "Binaries", "Win64");
                if (!Directory.Exists(binariesDirectory))
                {
                    continue;
                }

                foreach (var candidate in Directory.EnumerateFiles(binariesDirectory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    var preference = GetUnrealRootExecutablePreference(candidate, codeName);
                    if (preference > bestPreference)
                    {
                        continue;
                    }

                    if (preference == bestPreference && bestCandidate != null &&
                        string.Compare(Path.GetFileName(candidate), Path.GetFileName(bestCandidate), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    bestCandidate = candidate;
                    bestPreference = preference;
                }
            }

            return bestCandidate;
        }
        catch
        {
            return null;
        }
    }

    private static int GetExecutablePreference(string executablePath)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_)shipping$"))
        {
            return 0;
        }

        if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)$"))
        {
            return 1;
        }

        if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_)"))
        {
            return 2;
        }

        if (executableName.Contains("shipping", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (executableName.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
            executableName.Contains("crashreport", StringComparison.OrdinalIgnoreCase) ||
            executableName.Contains("bootstrap", StringComparison.OrdinalIgnoreCase) ||
            executableName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            executableName.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return 4;
    }

    private static int GetUnrealRootExecutablePreference(string executablePath, string codeName)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        var cleanedCodeName = CleanGameName(codeName);
        var cleanedExecutableName = CleanGameName(executableName);
        var startsWithCodeName = !string.IsNullOrWhiteSpace(cleanedCodeName) &&
            cleanedExecutableName.StartsWith(cleanedCodeName, StringComparison.OrdinalIgnoreCase);

        if (startsWithCodeName && Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_)shipping$"))
        {
            return 0;
        }

        if (startsWithCodeName && Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_|$)"))
        {
            return 1;
        }

        if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_)shipping$"))
        {
            return 2;
        }

        if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_|$)"))
        {
            return 3;
        }

        if (startsWithCodeName)
        {
            return 4;
        }

        return 5;
    }

    private static string GetPreferredGameDirectoryName(string gamePath)
    {
        var currentDirectory = new DirectoryInfo(gamePath);
        while (currentDirectory != null &&
               GenericGameDirectoryNames.Any(name => name.Equals(currentDirectory.Name, StringComparison.OrdinalIgnoreCase)))
        {
            currentDirectory = currentDirectory.Parent;
        }

        return currentDirectory?.Name ?? Path.GetFileName(gamePath);
    }

    private static string? TryGetExecutableMetadataName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            foreach (var candidate in new[] { info.ProductName, info.FileDescription })
            {
                var cleanedCandidate = CleanGameName(candidate ?? string.Empty);
                if (string.IsNullOrWhiteSpace(cleanedCandidate) || IsGenericExecutableMetadata(cleanedCandidate))
                {
                    continue;
                }

                return cleanedCandidate;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsGenericExecutableMetadata(string value)
    {
        return value.Equals("Bootstrap Packaged Game", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("UE4 Game", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("UE5 Game", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanGameName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();
        cleaned = Regex.Replace(cleaned, @"(?i)(?:[-_ ](?:win64|wingdk))?[-_ ]shipping$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?i)[-_ ](?:win64|wingdk)$", string.Empty);
        cleaned = cleaned.Replace('_', ' ').Replace('-', ' ');
        cleaned = Regex.Replace(cleaned, @"(?<=[A-Z])(?=[A-Z][a-z])", " ");
        cleaned = Regex.Replace(cleaned, @"(?<=[a-z0-9])(?=[A-Z])", " ");
        cleaned = Regex.Replace(cleaned, @"(?<=[A-Za-z])(?=[0-9])", " ");
        cleaned = Regex.Replace(cleaned, @"(?<=[0-9])(?=[A-Za-z])", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
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
        var isInstalled = _optiScalerService.IsInstalled(game.GamePath, out var filename, out var detectedVersion, out var fsrVersion, out var isOptiPatcherInstalled);
        ApplyInstallationState(game, isInstalled, filename, detectedVersion, fsrVersion, isOptiPatcherInstalled);
    }

    public async Task UpdateGameExecutable(GameInstance game, string rawExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(rawExecutablePath))
        {
            throw new InvalidOperationException("Select a valid .exe file.");
        }

        var executablePath = Path.GetFullPath(rawExecutablePath);
        if (!File.Exists(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a valid .exe file.");
        }

        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory) || !Directory.Exists(executableDirectory))
        {
            throw new InvalidOperationException("Could not determine the game folder from the selected executable.");
        }

        var normalizedPath = NormalizeGamePath(executableDirectory);
        if (Games.Any(existing =>
                !ReferenceEquals(existing, game) &&
                existing.GamePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That executable's folder is already in the library.");
        }

        var normalizedExecutablePath = NormalizeExecutablePath(executablePath, normalizedPath) ?? executablePath;
        var previousPath = NormalizeGamePath(game.GamePath);

        game.GamePath = normalizedPath;
        game.ExecutableName = Path.GetFileName(normalizedExecutablePath);
        game.Name = ResolveGameDisplayName(normalizedPath, normalizedExecutablePath);
        RefreshGameInstallation(game);

        var savedGames = GetSavedGames();
        var savedIndex = savedGames.FindIndex(saved =>
            NormalizeGamePath(saved.GamePath).Equals(previousPath, StringComparison.OrdinalIgnoreCase));

        var updatedEntry = new SavedGameEntry
        {
            GamePath = normalizedPath,
            ExecutablePath = normalizedExecutablePath,
        };

        if (savedIndex >= 0)
        {
            savedGames[savedIndex] = updatedEntry;
        }
        else
        {
            savedGames.Add(updatedEntry);
        }

        SaveSavedGames(savedGames);
        await _configService.SaveAsync();
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

        var previousIsInstalled = game.IsInstalled;
        var previousInstalledFilename = game.InstalledFilename;
        var previousVersion = game.CurrentVersion;
        var fallbackVersion = string.IsNullOrWhiteSpace(selectedVersion.TagName)
            ? previousVersion
            : selectedVersion.TagName;

        var isInstalled = _optiScalerService.IsInstalled(game.GamePath, out var installedFilename, out var newVersion, out var fsrVersion, out var isOptiPatcherInstalled);
        var redetectFailed = (!isInstalled && previousIsInstalled) ||
            string.IsNullOrWhiteSpace(installedFilename) ||
            string.IsNullOrWhiteSpace(newVersion);

        if (redetectFailed)
        {
            ApplyInstallationState(game, previousIsInstalled, previousInstalledFilename, fallbackVersion, string.Empty, false);
            return;
        }

        ApplyInstallationState(game, isInstalled, installedFilename, newVersion, fsrVersion, isOptiPatcherInstalled);
    }

    public async Task UninstallOptiScaler(GameInstance game)
    {
        if (!game.IsInstalled)
        {
            return;
        }

        await _optiScalerService.UninstallAsync(game.GamePath, game.InstalledFilename);
        ApplyInstallationState(game, false, string.Empty, string.Empty, string.Empty, false);
    }

    private static void ApplyInstallationState(
        GameInstance game,
        bool isInstalled,
        string installedFilename,
        string detectedVersion,
        string fsrVersion,
        bool isOptiPatcherInstalled)
    {
        game.IsInstalled = isInstalled;
        game.InstalledFilename = isInstalled ? installedFilename : string.Empty;
        game.CurrentVersion = isInstalled ? detectedVersion : "Not Installed";
        game.FsrVersion = isInstalled ? fsrVersion : string.Empty;
        game.IsOptiPatcherInstalled = isInstalled && isOptiPatcherInstalled;
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

        var path = NormalizeGamePath(game.GamePath);
        Games.Remove(game);

        var savedGames = GetSavedGames();
        if (savedGames.RemoveAll(saved =>
                NormalizeGamePath(saved.GamePath).Equals(path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            SaveSavedGames(savedGames);
            await _configService.SaveAsync();
        }
    }

    private async Task NormalizeSavedGamePathsAsync()
    {
        var normalizedGames = new List<SavedGameEntry>();
        foreach (var savedGame in GetSavedGames())
        {
            if (string.IsNullOrWhiteSpace(savedGame.GamePath))
            {
                continue;
            }

            var normalizedPath = NormalizeGamePath(savedGame.GamePath);
            if (normalizedGames.Any(existing => existing.GamePath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalizedGames.Add(new SavedGameEntry
            {
                GamePath = normalizedPath,
                ExecutablePath = NormalizeExecutablePath(savedGame.ExecutablePath, normalizedPath),
            });
        }

        var currentGames = GetSavedGames();
        var changed = currentGames.Count != normalizedGames.Count;
        if (!changed)
        {
            for (var i = 0; i < currentGames.Count; i++)
            {
                var currentGame = currentGames[i];
                var normalizedGame = normalizedGames[i];
                if (!currentGame.GamePath.Equals(normalizedGame.GamePath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(currentGame.ExecutablePath, normalizedGame.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        SaveSavedGames(normalizedGames);
        await _configService.SaveAsync();
    }

    private List<SavedGameEntry> GetSavedGames()
    {
        if (_configService.CurrentConfig.SavedGames.Count > 0)
        {
            return _configService.CurrentConfig.SavedGames
                .Select(saved => new SavedGameEntry
                {
                    GamePath = saved.GamePath,
                    ExecutablePath = saved.ExecutablePath,
                })
                .ToList();
        }

        return _configService.CurrentConfig.SavedGamePaths
            .Select(path => new SavedGameEntry
            {
                GamePath = path,
                ExecutablePath = null,
            })
            .ToList();
    }

    private void SaveSavedGames(IEnumerable<SavedGameEntry> savedGames)
    {
        var normalizedGames = savedGames
            .Where(saved => !string.IsNullOrWhiteSpace(saved.GamePath))
            .Select(saved => new SavedGameEntry
            {
                GamePath = saved.GamePath,
                ExecutablePath = string.IsNullOrWhiteSpace(saved.ExecutablePath) ? null : saved.ExecutablePath,
            })
            .ToList();

        _configService.CurrentConfig.SavedGames.Clear();
        _configService.CurrentConfig.SavedGames.AddRange(normalizedGames);

        _configService.CurrentConfig.SavedGamePaths.Clear();
        _configService.CurrentConfig.SavedGamePaths.AddRange(normalizedGames.Select(saved => saved.GamePath));
    }

    private static string? NormalizeExecutablePath(string? executablePath, string normalizedGamePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var fullExecutablePath = Path.GetFullPath(executablePath);
        var executableDirectory = Path.GetDirectoryName(fullExecutablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory) ||
            !NormalizeGamePath(executableDirectory).Equals(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var executableName = Path.GetFileName(fullExecutablePath);
        try
        {
            return Directory.EnumerateFiles(normalizedGamePath, executableName, SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? Path.Combine(normalizedGamePath, executableName);
        }
        catch
        {
            return Path.Combine(normalizedGamePath, executableName);
        }
    }

    private static string NormalizeGamePath(string path)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!OperatingSystem.IsWindows() || !Directory.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        var root = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrEmpty(root) || normalizedPath.Length <= root.Length)
        {
            return normalizedPath;
        }

        var currentPath = root;
        var segments = normalizedPath[root.Length..]
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var matchedPath = Directory.EnumerateFileSystemEntries(currentPath, segment).FirstOrDefault();
            currentPath = matchedPath ?? Path.Combine(currentPath, segment);
        }

        return currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
