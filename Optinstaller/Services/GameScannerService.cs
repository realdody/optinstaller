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
    private readonly SteamAppInfoService _steamAppInfoService = new();

    public IReadOnlyList<ScannedGame> ScanInstalledGames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<ScannedGame>();
        }

        var results = new Dictionary<string, ScannedGame>(StringComparer.OrdinalIgnoreCase);
        var steamAppInfoIndex = _steamAppInfoService.Load();

        Merge(results, ScanSteamGames(steamAppInfoIndex));
        Merge(results, ScanGogGames());
        Merge(results, ScanEpicGames());
        Merge(results, ScanEaDesktopGames());
        Merge(results, ScanOriginGames());
        Merge(results, ScanXboxGames());

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
