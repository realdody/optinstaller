using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameFinder.Common;
using GameFinder.RegistryUtils;
using GameFinder.StoreHandlers.EADesktop;
using GameFinder.StoreHandlers.EADesktop.Crypto.Windows;
using GameFinder.StoreHandlers.EGS;
using GameFinder.StoreHandlers.GOG;
using GameFinder.StoreHandlers.Origin;
using GameFinder.StoreHandlers.Steam;
using GameFinder.StoreHandlers.Xbox;
using NexusMods.Paths;

namespace Optinstaller.Services;

public sealed class GameScannerService
{
    private static readonly string[] IgnoredWatchedLibraryDirectoryNames =
    {
        "steamapps", "_CommonRedist", "CommonRedist", "DirectXRedist", "Redistributables", "Redist",
        "Support", "Tools", "Tool", "__Installer", "Installer", "Install", "Launcher", "Launchers",
        "EasyAntiCheat", "BattlEye", "Engine"
    };

    private static readonly string[] StructuralGameDirectoryNames =
    {
        "Binaries", "Binary", "Bin", "Win64", "Win32", "WinGDK", "x64", "x86", "Release", "Debug", "Retail"
    };

    private readonly SteamAppInfoService _steamAppInfoService = new();

    public IReadOnlyList<ScannedGame> ScanInstalledGames(IEnumerable<string>? watchedLibraryPaths = null)
    {
        var results = new Dictionary<string, ScannedGame>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            var steamAppInfoIndex = _steamAppInfoService.Load();

            Merge(results, ScanSteamGames(steamAppInfoIndex));
            Merge(results, ScanGogGames());
            Merge(results, ScanEpicGames());
            Merge(results, ScanEaDesktopGames());
            Merge(results, ScanOriginGames());
            Merge(results, ScanXboxGames());
        }

        Merge(results, ScanWatchedLibraries(watchedLibraryPaths ?? Array.Empty<string>()));

        return results.Values
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.InstallRootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Merge(IDictionary<string, ScannedGame> results, IEnumerable<ScannedGame> games)
    {
        foreach (var game in games)
        {
            if (results.TryGetValue(game.InstallRootPath, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.DisplayName) && !string.IsNullOrWhiteSpace(game.DisplayName))
                {
                    results[game.InstallRootPath] = game;
                }

                continue;
            }

            results[game.InstallRootPath] = game;
        }
    }

    private static IEnumerable<ScannedGame> ScanSteamGames(SteamAppInfoIndex steamAppInfoIndex)
    {
        try
        {
            var handler = new SteamHandler(FileSystem.Shared, WindowsRegistry.Shared, null!);
            return ExtractGames(handler.FindAllGames(), game =>
            {
                var installRootPath = ToNativePath(game.Path);
                var appId = game.AppId.ToString();
                var appInfoEntry = steamAppInfoIndex.Get(appId);
                if (appInfoEntry != null && !SteamAppInfoService.IsSupportedAppType(appInfoEntry.AppType))
                {
                    return null;
                }

                return new ScannedGame(
                    game.Name,
                    installRootPath,
                    "Steam",
                    appId,
                    SteamAppInfoService.ResolveExecutablePath(installRootPath, appInfoEntry));
            });
        }
        catch
        {
            return Array.Empty<ScannedGame>();
        }
    }

    private static IEnumerable<ScannedGame> ScanGogGames()
    {
        try
        {
            var handler = new GOGHandler(WindowsRegistry.Shared, FileSystem.Shared);
            return ExtractGames(
                handler.FindAllGames().Where(result => !result.TryGetGame(out GOGGame? game) || game.ParentGameId is null),
                game => new ScannedGame(game.Name, ToNativePath(game.Path), "GOG"));
        }
        catch
        {
            return Array.Empty<ScannedGame>();
        }
    }

    private static IEnumerable<ScannedGame> ScanEpicGames()
    {
        try
        {
            var handler = new EGSHandler(WindowsRegistry.Shared, FileSystem.Shared);
            return ExtractGames(handler.FindAllGames(), game => new ScannedGame(game.DisplayName, ToNativePath(game.InstallLocation), "Epic Games"));
        }
        catch
        {
            return Array.Empty<ScannedGame>();
        }
    }

    private static IEnumerable<ScannedGame> ScanEaDesktopGames()
    {
        try
        {
            var handler = new EADesktopHandler(FileSystem.Shared, new HardwareInfoProvider());
            return ExtractGames(handler.FindAllGames(), game => new ScannedGame(game.BaseSlug, ToNativePath(game.BaseInstallPath), "EA Desktop"));
        }
        catch
        {
            return Array.Empty<ScannedGame>();
        }
    }

    private static IEnumerable<ScannedGame> ScanOriginGames()
    {
        try
        {
            var handler = new OriginHandler(FileSystem.Shared);
            return ExtractGames(handler.FindAllGames(), game => new ScannedGame(string.Empty, ToNativePath(game.InstallPath), "Origin"));
        }
        catch
        {
            return Array.Empty<ScannedGame>();
        }
    }

    private static IEnumerable<ScannedGame> ScanXboxGames()
    {
        try
        {
            var handler = new XboxHandler(FileSystem.Shared);
            return ExtractGames(handler.FindAllGames(), game => new ScannedGame(game.DisplayName, ToNativePath(game.Path), "Xbox"));
        }
        catch
        {
            return Array.Empty<ScannedGame>();
        }
    }

    private static IEnumerable<ScannedGame> ScanWatchedLibraries(IEnumerable<string> watchedLibraryPaths)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var watchedLibraryPath in watchedLibraryPaths)
        {
            if (string.IsNullOrWhiteSpace(watchedLibraryPath))
            {
                continue;
            }

            string normalizedLibraryPath;
            try
            {
                normalizedLibraryPath = NormalizeDirectoryPath(watchedLibraryPath);
            }
            catch
            {
                continue;
            }

            foreach (var scanRoot in GetWatchedLibraryScanRoots(normalizedLibraryPath))
            {
                foreach (var candidatePath in EnumerateWatchedLibraryCandidates(scanRoot))
                {
                    if (!seenPaths.Add(candidatePath))
                    {
                        continue;
                    }

                    yield return new ScannedGame(string.Empty, candidatePath, "Watched Folder", normalizedLibraryPath);
                }
            }
        }
    }

    private static IEnumerable<ScannedGame> ExtractGames<TGame>(IEnumerable<OneOf.OneOf<TGame, ErrorMessage>> results, Func<TGame, ScannedGame?> selector)
        where TGame : class, IGame
    {
        foreach (var result in results)
        {
            if (!result.TryGetGame(out var game) || game == null)
            {
                continue;
            }

            var scannedGame = selector(game);
            if (scannedGame == null ||
                string.IsNullOrWhiteSpace(scannedGame.InstallRootPath) ||
                !Directory.Exists(scannedGame.InstallRootPath))
            {
                continue;
            }

            yield return scannedGame;
        }
    }

    private static IEnumerable<string> GetWatchedLibraryScanRoots(string libraryPath)
    {
        if (!Directory.Exists(libraryPath))
        {
            yield break;
        }

        var leafDirectoryName = Path.GetFileName(libraryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(leafDirectoryName, "common", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(Path.GetDirectoryName(libraryPath) ?? string.Empty), "steamapps", StringComparison.OrdinalIgnoreCase))
        {
            yield return libraryPath;
            yield break;
        }

        var directCommonPath = Path.Combine(libraryPath, "common");
        if (string.Equals(leafDirectoryName, "steamapps", StringComparison.OrdinalIgnoreCase) && Directory.Exists(directCommonPath))
        {
            yield return NormalizeDirectoryPath(directCommonPath);
            yield break;
        }

        var steamCommonPath = Path.Combine(libraryPath, "steamapps", "common");
        if (Directory.Exists(steamCommonPath))
        {
            yield return NormalizeDirectoryPath(steamCommonPath);
            yield break;
        }

        yield return libraryPath;
    }

    private static IEnumerable<string> EnumerateWatchedLibraryCandidates(string scanRoot)
    {
        if (!Directory.Exists(scanRoot))
        {
            yield break;
        }

        if (LooksLikeStandaloneGameDirectory(scanRoot))
        {
            yield return scanRoot;
        }

        foreach (var childDirectory in EnumerateWatchedLibraryDirectories(scanRoot))
        {
            yield return childDirectory;

            if (LooksLikeStandaloneGameDirectory(childDirectory))
            {
                continue;
            }

            foreach (var grandChildDirectory in EnumerateWatchedLibraryDirectories(childDirectory))
            {
                yield return grandChildDirectory;
            }
        }
    }

    private static IEnumerable<string> EnumerateWatchedLibraryDirectories(string directoryPath)
    {
        IEnumerable<string> childDirectories;
        try
        {
            childDirectories = Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var childDirectory in childDirectories)
        {
            var directoryName = Path.GetFileName(childDirectory);
            if (string.IsNullOrWhiteSpace(directoryName) ||
                IgnoredWatchedLibraryDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return NormalizeDirectoryPath(childDirectory);
        }
    }

    private static bool LooksLikeStandaloneGameDirectory(string directoryPath)
    {
        try
        {
            if (Directory.EnumerateFiles(directoryPath, "*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                var directoryName = Path.GetFileName(childDirectory);
                if (string.IsNullOrWhiteSpace(directoryName) ||
                    !StructuralGameDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (Directory.EnumerateFiles(childDirectory, "*.exe", SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }

                foreach (var grandChildDirectory in Directory.EnumerateDirectories(childDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (Directory.EnumerateFiles(grandChildDirectory, "*.exe", SearchOption.TopDirectoryOnly).Any())
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ToNativePath(AbsolutePath path)
    {
        return Path.GetFullPath(path.ToString())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed class ScannedGame
{
    public ScannedGame(string displayName, string installRootPath, string source)
        : this(displayName, installRootPath, source, null, null)
    {
    }

    public ScannedGame(string displayName, string installRootPath, string source, string? sourceId)
        : this(displayName, installRootPath, source, sourceId, null)
    {
    }

    public ScannedGame(string displayName, string installRootPath, string source, string? sourceId, string? preferredExecutablePath)
    {
        DisplayName = displayName;
        InstallRootPath = installRootPath;
        Source = source;
        SourceId = sourceId;
        PreferredExecutablePath = preferredExecutablePath;
    }

    public string DisplayName { get; }

    public string InstallRootPath { get; }

    public string Source { get; }

    public string? SourceId { get; }

    public string? PreferredExecutablePath { get; }
}
