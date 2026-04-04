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

    private static readonly string[] CandidateExecutableDirectoryNames =
    {
        "Binaries", "Binary", "Bin", "Win64", "Win32", "WinGDK", "x64", "x86", "Release", "Retail"
    };

    private static readonly string[] IgnoredExecutableDirectoryNames =
    {
        "Engine", "__Installer", "Installer", "Install", "_CommonRedist", "CommonRedist", "DirectXRedist",
        "Redistributables", "Redist", "Support", "Tools", "Tool", "Launcher", "Launchers", "CrashReportClient",
        "EasyAntiCheat", "BattlEye", "ThirdParty"
    };

    private static readonly string[] IgnoredExecutableNameTokens =
    {
        "launcher", "launch", "crashreport", "crashreporter", "bootstrap", "setup", "uninstall", "helper",
        "patcher", "updater", "report", "unitycrashhandler", "easyanticheat", "eac", "benchmark", "vc_redist",
        "compiler", "editor", "createdump", "sdk", "dedicatedserver", "dedicated", "server",
        "bugsplat", "bssndrpt", "splat"
    };

    private static readonly string[] IgnoredExecutableMetadataTokens =
    {
        "crash report",
        "crashreport",
        "crash report utility",
        "launcher",
        "bootstrap",
        "helper",
        "updater",
        "installer",
        "uninstall",
        "setup",
        "benchmark",
        "diagnostic",
        "configuration tool",
        "configurator",
        "compiler",
        "editor",
        "dedicated server",
        "bugsplat",
        "hang detection",
        "report sender",
    };

    private static readonly string[] SidecarUpscalerDllNames =
    {
        "amd_fidelityfx_upscaler_dx12.dll",
        "amd_fidelityfx_framegeneration_dx12.dll",
        "libxess.dll",
        "libxess_fg.dll",
        "nvngx_dlss.dll",
        "nvngx_dlssg.dll",
        "sl.interposer.dll",
        "sl.common.dll",
        "sl.dlss.dll",
        "sl.dlss_g.dll",
        "sl.reflex.dll",
    };

    private readonly OptiScalerService _optiScalerService;
    private readonly AntiCheatDetectionService _antiCheatDetectionService;
    private readonly VersionService _versionService;
    private readonly ConfigurationService _configService;
    private readonly GameScannerService _gameScannerService;
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
        _antiCheatDetectionService = new AntiCheatDetectionService();
        _versionService = new VersionService();
        _configService = new ConfigurationService();
        _gameScannerService = new GameScannerService();

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
        await Task.WhenAll(
            _configService.LoadAsync(),
            _antiCheatDetectionService.InitializeAsync());
        await NormalizeSavedGamePathsAsync();
        await RefreshVersions();
        await LoadGamesAsync();
    }

    private async Task LoadGamesAsync()
    {
        Games.Clear();

        var gamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var savedGame in GetSavedGames())
        {
            if (Directory.Exists(savedGame.GamePath))
            {
                var game = CreateGameInternal(savedGame.GamePath, savedGame.ExecutablePath);
                if (game == null || !gamePaths.Add(game.GamePath))
                {
                    continue;
                }

                Games.Add(game);
            }
        }

        var hiddenScannedGamePaths = GetHiddenScannedGamePaths();
        var scannedGames = await Task.Run(() => _gameScannerService.ScanInstalledGames());
        foreach (var scannedGame in scannedGames)
        {
            var game = CreateGameInternal(
                scannedGame.InstallRootPath,
                preferredDisplayName: scannedGame.DisplayName,
                scanSource: scannedGame.Source,
                scanSourceId: scannedGame.SourceId,
                preferredExecutablePathHint: scannedGame.PreferredExecutablePath);
            if (game == null ||
                hiddenScannedGamePaths.Contains(game.GamePath) ||
                !gamePaths.Add(game.GamePath))
            {
                continue;
            }

            Games.Add(game);
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

        var game = CreateGameInternal(Path.GetDirectoryName(executablePath) ?? string.Empty, executablePath);
        if (game == null)
        {
            throw new InvalidOperationException("Could not determine the game folder from the selected executable.");
        }

        if (Games.Any(existing => existing.GamePath.Equals(game.GamePath, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        Games.Add(game);

        var savedGames = GetSavedGames();
        if (!savedGames.Any(saved =>
                NormalizeGamePath(saved.GamePath).Equals(game.GamePath, StringComparison.OrdinalIgnoreCase)))
        {
            savedGames.Add(new SavedGameEntry
            {
                GamePath = game.GamePath,
                ExecutablePath = NormalizeExecutablePath(executablePath, game.GamePath),
            });
        }

        var hiddenPathsChanged = RemoveHiddenScannedGamePath(game.GamePath);
        if (hiddenPathsChanged ||
            savedGames.Any(saved => NormalizeGamePath(saved.GamePath).Equals(game.GamePath, StringComparison.OrdinalIgnoreCase)))
        {
            SaveSavedGames(savedGames);
            await _configService.SaveAsync();
        }

        return game;
    }

    private GameInstance AddGameInternal(string path, string? executablePath = null, string? preferredDisplayName = null, string? scanSource = null, string? scanSourceId = null, string? preferredExecutablePathHint = null)
    {
        var game = CreateGameInternal(path, executablePath, preferredDisplayName, scanSource, scanSourceId, preferredExecutablePathHint)
            ?? throw new InvalidOperationException("Could not determine a usable executable for the selected game.");

        Games.Add(game);
        return game;
    }

    private GameInstance? CreateGameInternal(string path, string? executablePath = null, string? preferredDisplayName = null, string? scanSource = null, string? scanSourceId = null, string? preferredExecutablePathHint = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        var normalizedPath = NormalizeGamePath(path);
        var resolvedExecutablePath = ResolveExecutablePath(normalizedPath, executablePath, preferredDisplayName, preferredExecutablePathHint);
        var effectiveGamePath = normalizedPath;
        if (!string.IsNullOrWhiteSpace(resolvedExecutablePath))
        {
            var executableDirectory = Path.GetDirectoryName(resolvedExecutablePath);
            if (!string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory))
            {
                effectiveGamePath = NormalizeGamePath(executableDirectory);
            }
        }

        var displayName = ResolveGameDisplayName(effectiveGamePath, resolvedExecutablePath, preferredDisplayName);

        var isInstalled = _optiScalerService.IsInstalled(effectiveGamePath, out var installedFilename, out var detectedVersion, out var fsrVersion, out var isOptiPatcherInstalled);

        var game = new GameInstance
        {
            Name = displayName,
            GamePath = effectiveGamePath,
            ExecutableName = resolvedExecutablePath == null ? string.Empty : Path.GetFileName(resolvedExecutablePath),
            AntiCheatProvider = _antiCheatDetectionService.Detect(effectiveGamePath, displayName, scanSource, scanSourceId),
            ScanSource = scanSource ?? string.Empty,
            ScanSourceId = scanSourceId ?? string.Empty,
        };

        ApplyInstallationState(game, isInstalled, installedFilename, detectedVersion, fsrVersion, isOptiPatcherInstalled);
        return game;
    }

    private static string ResolveGameDisplayName(string gamePath, string? executablePath, string? preferredDisplayName = null)
    {
        var metadataName = TryGetExecutableMetadataName(executablePath);
        if (!string.IsNullOrWhiteSpace(metadataName))
        {
            return metadataName;
        }

        var cleanedPreferredName = CleanGameName(preferredDisplayName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(cleanedPreferredName))
        {
            return cleanedPreferredName;
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

    private static string? ResolveExecutablePath(string gamePath, string? explicitExecutablePath, string? preferredDisplayName = null, string? preferredExecutablePathHint = null)
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
            var expectedNames = GetExpectedExecutableNames(gamePath, preferredDisplayName, preferredExecutablePathHint);
            var executablePaths = EnumerateExecutableCandidates(gamePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (executablePaths.Count == 0)
            {
                return null;
            }

            var sidecarExecutablePaths = executablePaths
                .Where(path => HasKnownUpscalerSidecars(path))
                .ToList();
            if (sidecarExecutablePaths.Count > 0)
            {
                executablePaths = sidecarExecutablePaths;
            }

            var nonUtilityExecutablePaths = executablePaths
                .Where(path => !LooksLikeIgnoredExecutable(path))
                .ToList();
            if (nonUtilityExecutablePaths.Count > 0)
            {
                executablePaths = nonUtilityExecutablePaths;
            }

            var largestExecutableSize = executablePaths
                .Select(GetExecutableFileSize)
                .DefaultIfEmpty(0)
                .Max();

            return executablePaths
                .OrderBy(path => GetExecutablePreference(path, gamePath, expectedNames, largestExecutableSize))
                .ThenBy(path => GetRelativePathDepth(gamePath, path))
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

        var searchRoot = GetExecutableSearchRoot(executableDirectory);
        var selectedExecutableName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.Equals(searchRoot, executableDirectory, StringComparison.OrdinalIgnoreCase) &&
            !LooksLikeIgnoredExecutable(executablePath) &&
            !HasGenericExecutableMetadata(executablePath))
        {
            return executablePath;
        }

        return ResolveExecutablePath(searchRoot, null, selectedExecutableName) ?? executablePath;
    }

    private static IReadOnlyCollection<string> GetExpectedExecutableNames(string gamePath, string? preferredDisplayName, string? preferredExecutablePathHint = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddExpectedExecutableName(names, preferredDisplayName);
        AddExpectedExecutableName(names, Path.GetFileNameWithoutExtension(preferredExecutablePathHint));
        AddExpectedExecutableName(names, GetPreferredGameDirectoryName(gamePath));
        AddExpectedExecutableName(names, Path.GetFileName(gamePath));

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(gamePath))
            {
                var directoryName = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(directoryName) ||
                    IgnoredExecutableDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) ||
                    CandidateExecutableDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                AddExpectedExecutableName(names, directoryName);
            }
        }
        catch
        {
        }

        return names;
    }

    private static void AddExpectedExecutableName(HashSet<string> names, string? value)
    {
        var cleanedValue = CleanGameName(value ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(cleanedValue))
        {
            names.Add(cleanedValue);
        }
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(string gamePath)
    {
        var pendingDirectories = new Stack<(string Path, int Depth)>();
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pendingDirectories.Push((gamePath, 0));

        while (pendingDirectories.Count > 0)
        {
            var (currentDirectory, depth) = pendingDirectories.Pop();
            if (!visitedDirectories.Add(currentDirectory))
            {
                continue;
            }

            IEnumerable<string> executablePaths;
            try
            {
                executablePaths = Directory.EnumerateFiles(currentDirectory, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                executablePaths = Array.Empty<string>();
            }

            foreach (var executablePath in executablePaths)
            {
                yield return Path.GetFullPath(executablePath);
            }

            if (depth >= 4)
            {
                continue;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories
                         .Where(path => ShouldSearchExecutableDirectory(path, depth))
                         .OrderByDescending(path => GetExecutableDirectorySearchPriority(Path.GetFileName(path))))
            {
                pendingDirectories.Push((childDirectory, depth + 1));
            }
        }
    }

    private static bool ShouldSearchExecutableDirectory(string directoryPath, int depth)
    {
        var directoryName = Path.GetFileName(directoryPath);
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        if (IgnoredExecutableDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return depth <= 1 || IsLikelyExecutableDirectory(directoryName);
    }

    private static bool IsLikelyExecutableDirectory(string directoryName)
    {
        return CandidateExecutableDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetExecutableDirectorySearchPriority(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return 0;
        }

        if (directoryName.Equals("Win64", StringComparison.OrdinalIgnoreCase) ||
            directoryName.Equals("WinGDK", StringComparison.OrdinalIgnoreCase) ||
            directoryName.Equals("x64", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (directoryName.Equals("Binaries", StringComparison.OrdinalIgnoreCase) ||
            directoryName.Equals("Bin", StringComparison.OrdinalIgnoreCase) ||
            directoryName.Equals("Binary", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (directoryName.Equals("Release", StringComparison.OrdinalIgnoreCase) ||
            directoryName.Equals("Retail", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static int GetExecutablePreference(string executablePath, string gamePath, IReadOnlyCollection<string> expectedNames, long largestExecutableSize)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        var cleanedExecutableName = CleanGameName(executableName);
        var score = 4000 + (GetRelativePathDepth(gamePath, executablePath) * 100);

        if (MatchesExpectedNameExactly(cleanedExecutableName, expectedNames))
        {
            score -= 2200;
        }
        else if (StartsWithExpectedName(cleanedExecutableName, expectedNames))
        {
            score -= 1600;
        }
        else if (ContainsExpectedName(cleanedExecutableName, expectedNames))
        {
            score -= 900;
        }

        if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_)shipping$"))
        {
            score -= 1700;
        }
        else if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)$"))
        {
            score -= 1400;
        }
        else if (Regex.IsMatch(executableName, @"(?i)(?:-|_)(win64|wingdk)(?:-|_|$)"))
        {
            score -= 1100;
        }
        else if (executableName.Contains("shipping", StringComparison.OrdinalIgnoreCase))
        {
            score -= 800;
        }

        score -= GetExecutableDirectoryBonus(gamePath, executablePath);
        score -= GetSidecarUpscalerBonus(executablePath);
        score += GetExecutableSizeAdjustment(executablePath, largestExecutableSize);

        if (MatchesExecutableMetadata(executablePath, expectedNames))
        {
            score -= 500;
        }

        if (HasGenericExecutableMetadata(executablePath))
        {
            score += 1200;
        }

        if (LooksLikeIgnoredExecutable(executablePath))
        {
            score += 2500;
        }

        return score;
    }

    private static int GetExecutableSizeAdjustment(string executablePath, long largestExecutableSize)
    {
        var fileSize = GetExecutableFileSize(executablePath);
        if (fileSize <= 0 || largestExecutableSize <= 0)
        {
            return 0;
        }

        if (largestExecutableSize >= 1_000_000 && fileSize < 512_000)
        {
            return 1200;
        }

        if (fileSize * 4 < largestExecutableSize)
        {
            return 800;
        }

        if (fileSize * 2 < largestExecutableSize)
        {
            return 350;
        }

        if (fileSize == largestExecutableSize)
        {
            return -300;
        }

        if (fileSize * 10 >= largestExecutableSize * 9)
        {
            return -180;
        }

        if (fileSize * 10 >= largestExecutableSize * 6)
        {
            return -80;
        }

        return 0;
    }

    private static long GetExecutableFileSize(string executablePath)
    {
        try
        {
            return new FileInfo(executablePath).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static int GetExecutableDirectoryBonus(string gamePath, string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            return 0;
        }

        var relativeDirectory = Path.GetRelativePath(gamePath, executableDirectory);
        if (string.IsNullOrWhiteSpace(relativeDirectory) || relativeDirectory.Equals(".", StringComparison.Ordinal))
        {
            return 700;
        }

        var parts = relativeDirectory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var hasBinaries = parts.Any(part => part.Equals("Binaries", StringComparison.OrdinalIgnoreCase) ||
                                            part.Equals("Bin", StringComparison.OrdinalIgnoreCase) ||
                                            part.Equals("Binary", StringComparison.OrdinalIgnoreCase));
        var has64BitDirectory = parts.Any(part => part.Equals("Win64", StringComparison.OrdinalIgnoreCase) ||
                                                  part.Equals("WinGDK", StringComparison.OrdinalIgnoreCase) ||
                                                  part.Equals("x64", StringComparison.OrdinalIgnoreCase));

        if (hasBinaries && has64BitDirectory)
        {
            return 1200;
        }

        if (has64BitDirectory)
        {
            return 900;
        }

        if (parts.Any(part => part.Equals("Release", StringComparison.OrdinalIgnoreCase) ||
                              part.Equals("Retail", StringComparison.OrdinalIgnoreCase)))
        {
            return 500;
        }

        return 0;
    }

    private static int GetSidecarUpscalerBonus(string executablePath)
    {
        return GetKnownUpscalerSidecarCount(executablePath) switch
        {
            >= 3 => 1800,
            2 => 1300,
            1 => 800,
            _ => 0,
        };
    }

    private static bool HasKnownUpscalerSidecars(string executablePath)
    {
        return GetKnownUpscalerSidecarCount(executablePath) > 0;
    }

    private static int GetKnownUpscalerSidecarCount(string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory) || !Directory.Exists(executableDirectory))
        {
            return 0;
        }

        var sidecarCount = 0;
        foreach (var dllName in SidecarUpscalerDllNames)
        {
            if (File.Exists(Path.Combine(executableDirectory, dllName)))
            {
                sidecarCount++;
            }
        }

        return sidecarCount;
    }

    private static bool MatchesExpectedNameExactly(string candidateName, IReadOnlyCollection<string> expectedNames)
    {
        return expectedNames.Any(expectedName =>
            candidateName.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithExpectedName(string candidateName, IReadOnlyCollection<string> expectedNames)
    {
        return expectedNames.Any(expectedName =>
            candidateName.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsExpectedName(string candidateName, IReadOnlyCollection<string> expectedNames)
    {
        return expectedNames.Any(expectedName =>
            candidateName.Contains(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesExecutableMetadata(string executablePath, IReadOnlyCollection<string> expectedNames)
    {
        var metadataName = TryGetExecutableMetadataName(executablePath);
        if (string.IsNullOrWhiteSpace(metadataName))
        {
            return false;
        }

        return MatchesExpectedNameExactly(metadataName, expectedNames) ||
               StartsWithExpectedName(metadataName, expectedNames) ||
               ContainsExpectedName(metadataName, expectedNames);
    }

    private static bool HasGenericExecutableMetadata(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return false;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return IsGenericExecutableMetadata(info.ProductName ?? string.Empty) ||
                   IsGenericExecutableMetadata(info.FileDescription ?? string.Empty);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeIgnoredExecutable(string executablePath)
    {
        var executableName = File.Exists(executablePath)
            ? Path.GetFileNameWithoutExtension(executablePath)
            : executablePath;

        if (IgnoredExecutableNameTokens.Any(token =>
                executableName.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var metadataValue in GetExecutableMetadataValues(executablePath))
        {
            if (IgnoredExecutableMetadataTokens.Any(token =>
                    metadataValue.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetExecutableMetadataValues(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            yield break;
        }

        FileVersionInfo? info = null;
        try
        {
            info = FileVersionInfo.GetVersionInfo(executablePath);
        }
        catch
        {
        }

        if (info == null)
        {
            yield break;
        }

        foreach (var value in new[] { info.ProductName, info.FileDescription, info.InternalName, info.OriginalFilename })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.Trim();
            }
        }
    }

    private static int GetRelativePathDepth(string gamePath, string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            return 0;
        }

        var relativeDirectory = Path.GetRelativePath(gamePath, executableDirectory);
        if (string.IsNullOrWhiteSpace(relativeDirectory) || relativeDirectory.Equals(".", StringComparison.Ordinal))
        {
            return 0;
        }

        return relativeDirectory
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static string GetExecutableSearchRoot(string executableDirectory)
    {
        var currentDirectory = new DirectoryInfo(executableDirectory);
        while (currentDirectory.Parent != null && IsStructuralGameDirectory(currentDirectory.Name))
        {
            currentDirectory = currentDirectory.Parent;
        }

        return currentDirectory.FullName;
    }

    private static bool IsStructuralGameDirectory(string directoryName)
    {
        return GenericGameDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) ||
               CandidateExecutableDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) ||
               IgnoredExecutableDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase));
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
                if (LooksLikeUnreadableMetadata(candidate))
                {
                    continue;
                }

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

    private static bool LooksLikeUnreadableMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains('\uFFFD') ||
               value.Contains("ï¿½", StringComparison.Ordinal) ||
               value.Contains("���", StringComparison.Ordinal);
    }

    private static bool IsGenericExecutableMetadata(string value)
    {
        var normalizedValue = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalizedValue.Equals("Bootstrap Packaged Game", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Equals("UE4 Game", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Equals("UE 4 Game", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Equals("UE5 Game", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Equals("UE 5 Game", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase);
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
        game.AntiCheatProvider = _antiCheatDetectionService.Detect(normalizedPath, game.Name, game.ScanSource, game.ScanSourceId);
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

        RemoveHiddenScannedGamePath(normalizedPath);
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
        var removedSavedGames = savedGames.RemoveAll(saved =>
                NormalizeGamePath(saved.GamePath).Equals(path, StringComparison.OrdinalIgnoreCase)) > 0;
        var addedHiddenPath = AddHiddenScannedGamePath(path);
        if (removedSavedGames || addedHiddenPath)
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

            var normalizedGame = NormalizeSavedGameEntry(savedGame);
            if (normalizedGames.Any(existing => existing.GamePath.Equals(normalizedGame.GamePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalizedGames.Add(normalizedGame);
        }

        var normalizedHiddenPaths = _configService.CurrentConfig.HiddenScannedGamePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeGamePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

        var currentHiddenPaths = _configService.CurrentConfig.HiddenScannedGamePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        if (!changed)
        {
            changed = currentHiddenPaths.Count != normalizedHiddenPaths.Count ||
                      !currentHiddenPaths.SequenceEqual(normalizedHiddenPaths, StringComparer.OrdinalIgnoreCase);
        }

        if (!changed)
        {
            return;
        }

        SaveSavedGames(normalizedGames);
        _configService.CurrentConfig.HiddenScannedGamePaths.Clear();
        _configService.CurrentConfig.HiddenScannedGamePaths.AddRange(normalizedHiddenPaths);
        await _configService.SaveAsync();
    }

    private static SavedGameEntry NormalizeSavedGameEntry(SavedGameEntry savedGame)
    {
        var normalizedPath = NormalizeGamePath(savedGame.GamePath);
        string? normalizedExecutablePath = null;

        if (!string.IsNullOrWhiteSpace(savedGame.ExecutablePath) && File.Exists(savedGame.ExecutablePath))
        {
            var fullExecutablePath = Path.GetFullPath(savedGame.ExecutablePath);
            var executableDirectory = Path.GetDirectoryName(fullExecutablePath);
            if (!string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory))
            {
                normalizedPath = NormalizeGamePath(executableDirectory);
                normalizedExecutablePath = NormalizeExecutablePath(fullExecutablePath, normalizedPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedExecutablePath))
        {
            return new SavedGameEntry
            {
                GamePath = normalizedPath,
                ExecutablePath = normalizedExecutablePath,
            };
        }

        var resolvedExecutablePath = ResolveExecutablePath(normalizedPath, null);
        if (!string.IsNullOrWhiteSpace(resolvedExecutablePath))
        {
            var executableDirectory = Path.GetDirectoryName(resolvedExecutablePath);
            if (!string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory))
            {
                normalizedPath = NormalizeGamePath(executableDirectory);
                normalizedExecutablePath = NormalizeExecutablePath(resolvedExecutablePath, normalizedPath);
            }
        }

        return new SavedGameEntry
        {
            GamePath = normalizedPath,
            ExecutablePath = normalizedExecutablePath,
        };
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

    private HashSet<string> GetHiddenScannedGamePaths()
    {
        return _configService.CurrentConfig.HiddenScannedGamePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeGamePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool AddHiddenScannedGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizeGamePath(path);
        if (_configService.CurrentConfig.HiddenScannedGamePaths.Any(existing =>
                NormalizeGamePath(existing).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _configService.CurrentConfig.HiddenScannedGamePaths.Add(normalizedPath);
        return true;
    }

    private bool RemoveHiddenScannedGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizeGamePath(path);
        return _configService.CurrentConfig.HiddenScannedGamePaths.RemoveAll(existing =>
            NormalizeGamePath(existing).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)) > 0;
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
