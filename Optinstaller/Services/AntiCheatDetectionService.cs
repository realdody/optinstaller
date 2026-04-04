using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Optinstaller.Services;

public sealed class AntiCheatDetectionService
{
    private const string CatalogUrl = "https://raw.githubusercontent.com/AreWeAntiCheatYet/AreWeAntiCheatYet/master/games.json";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static readonly string[] StructuralDirectoryNames =
    {
        "Binaries", "Binary", "Bin", "Win64", "Win32", "WinGDK", "x64", "x86", "Release", "Debug", "Retail", "Engine"
    };

    private static readonly AntiCheatSignature[] Signatures =
    {
        new(
            "Easy Anti-Cheat",
            new[] { "EasyAntiCheat", "EasyAntiCheat_EOS" },
            new[]
            {
                "start_protected_game.exe",
                "easyanticheat_launcher.exe",
                "EasyAntiCheat\\EasyAntiCheat.exe",
                "EasyAntiCheat\\EasyAntiCheat_EOS.exe",
                "EasyAntiCheat_EOS\\EasyAntiCheat_EOS.exe",
                "EasyAntiCheat\\EasyAntiCheat_x64.dll",
                "EasyAntiCheat\\EasyAntiCheat_EOS_Setup.exe",
            }),
        new(
            "BattlEye",
            new[] { "BattlEye" },
            new[]
            {
                "BattlEye\\BEService.exe",
                "BattlEye\\BEClient_x64.dll",
                "BattlEye\\BEClient_x86.dll",
                "BEService.exe",
                "BEClient_x64.dll",
            }),
        new(
            "EA AntiCheat",
            new[] { "EAAntiCheat" },
            new[]
            {
                "EAAntiCheat\\EAAntiCheat.GameServiceLauncher.exe",
                "EAAntiCheat\\EAAntiCheat.Installer.exe",
                "EAAntiCheat\\Installer\\EAAntiCheat.Installer.exe",
            }),
        new(
            "XIGNCODE3",
            new[] { "xigncode", "xigncode3", "XIGNCODE", "XIGNCODE3" },
            new[]
            {
                "x3.xem",
                "xcorona.xem",
                "xem\\x3.xem",
                "xigncode\\x3.xem",
            }),
        new(
            "nProtect GameGuard",
            new[] { "GameGuard", "nProtect" },
            new[]
            {
                "GameMon.des",
                "npgl.erl",
                "GameGuard\\GameMon.des",
                "nProtect\\GameMon.des",
            }),
        new(
            "PunkBuster",
            new[] { "pb", "PunkBuster" },
            new[]
            {
                "PnkBstrA.exe",
                "PnkBstrB.exe",
                "pbsvc.exe",
                "PunkBuster\\PnkBstrA.exe",
            }),
    };

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initializationAttempted;
    private Dictionary<string, AntiCheatCatalogEntry> _entriesBySteamId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AntiCheatCatalogEntry> _entriesByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AntiCheatCatalogEntry> _entriesBySlug = new(StringComparer.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (_initializationAttempted)
        {
            return;
        }

        await _initializationLock.WaitAsync();
        try
        {
            if (_initializationAttempted)
            {
                return;
            }

            _initializationAttempted = true;

            try
            {
                var entries = await SharedHttpClient.GetFromJsonAsync(CatalogUrl, AntiCheatDetectionJsonContext.Default.ListAntiCheatCatalogEntry);
                if (entries != null)
                {
                    IndexCatalog(entries);
                }
            }
            catch
            {
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public string Detect(string gamePath, string? displayName = null, string? source = null, string? sourceId = null)
    {
        var providers = new List<string>();
        var seenProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddProviders(providers, seenProviders, DetectLocalProviders(gamePath));

        var catalogEntry = FindCatalogEntry(displayName, source, sourceId, gamePath);
        if (catalogEntry?.Anticheats != null)
        {
            AddProviders(providers, seenProviders, catalogEntry.Anticheats);
        }

        return providers.Count == 0 ? string.Empty : string.Join(", ", providers);
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Optinstaller/1.0 (OptiScaler Manager)");
        return httpClient;
    }

    private void IndexCatalog(IEnumerable<AntiCheatCatalogEntry> entries)
    {
        _entriesBySteamId = new Dictionary<string, AntiCheatCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        _entriesByName = new Dictionary<string, AntiCheatCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        _entriesBySlug = new Dictionary<string, AntiCheatCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.StoreIds?.Steam) && !_entriesBySteamId.ContainsKey(entry.StoreIds.Steam))
            {
                _entriesBySteamId[entry.StoreIds.Steam] = entry;
            }

            var normalizedName = NormalizeLookupValue(entry.Name);
            if (!string.IsNullOrWhiteSpace(normalizedName) && !_entriesByName.ContainsKey(normalizedName))
            {
                _entriesByName[normalizedName] = entry;
            }

            var normalizedSlug = NormalizeLookupValue(entry.Slug);
            if (!string.IsNullOrWhiteSpace(normalizedSlug) && !_entriesBySlug.ContainsKey(normalizedSlug))
            {
                _entriesBySlug[normalizedSlug] = entry;
            }
        }
    }

    private IEnumerable<string> DetectLocalProviders(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return Array.Empty<string>();
        }

        var probeRoots = GetProbeRoots(gamePath);
        return Signatures
            .Where(signature => probeRoots.Any(signature.IsMatch))
            .Select(signature => signature.Name)
            .ToList();
    }

    private AntiCheatCatalogEntry? FindCatalogEntry(string? displayName, string? source, string? sourceId, string gamePath)
    {
        if (string.Equals(source, "Steam", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(sourceId) &&
            _entriesBySteamId.TryGetValue(sourceId, out var steamEntry))
        {
            return steamEntry;
        }

        foreach (var candidate in GetLookupCandidates(displayName, gamePath))
        {
            var normalizedCandidate = NormalizeLookupValue(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                continue;
            }

            if (_entriesByName.TryGetValue(normalizedCandidate, out var nameEntry))
            {
                return nameEntry;
            }

            if (_entriesBySlug.TryGetValue(normalizedCandidate, out var slugEntry))
            {
                return slugEntry;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetLookupCandidates(string? displayName, string gamePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(displayName) && seen.Add(displayName))
        {
            yield return displayName;
        }

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            yield break;
        }

        var current = new DirectoryInfo(Path.GetFullPath(gamePath));
        for (var depth = 0; depth < 4 && current != null; depth++)
        {
            if (!StructuralDirectoryNames.Any(name => name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(current.Name) &&
                seen.Add(current.Name))
            {
                yield return current.Name;
            }

            current = current.Parent;
        }
    }

    private static string NormalizeLookupValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();
        cleaned = Regex.Replace(cleaned, @"(?<=[A-Z])(?=[A-Z][a-z])", " ");
        cleaned = Regex.Replace(cleaned, @"(?<=[a-z0-9])(?=[A-Z])", " ");
        cleaned = cleaned.Replace('_', ' ').Replace('-', ' ');
        cleaned = Regex.Replace(cleaned, @"(?i)\b(win64|win32|wingdk|shipping|client|server)\b", " ");
        cleaned = Regex.Replace(cleaned, @"(?i)\bgame\b$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"[^A-Za-z0-9]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.ToLowerInvariant();
    }

    private static IReadOnlyList<string> GetProbeRoots(string gamePath)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(Path.GetFullPath(gamePath));

        for (var depth = 0; depth < 4 && current != null; depth++)
        {
            if (seen.Add(current.FullName))
            {
                roots.Add(current.FullName);
            }

            current = current.Parent;
        }

        return roots;
    }

    private static void AddProviders(List<string> providers, HashSet<string> seenProviders, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seenProviders.Add(value))
            {
                continue;
            }

            providers.Add(value);
        }
    }

    private sealed class AntiCheatSignature
    {
        public AntiCheatSignature(string name, IReadOnlyList<string> directoryMarkers, IReadOnlyList<string> fileMarkers)
        {
            Name = name;
            DirectoryMarkers = directoryMarkers;
            FileMarkers = fileMarkers;
        }

        public string Name { get; }

        private IReadOnlyList<string> DirectoryMarkers { get; }

        private IReadOnlyList<string> FileMarkers { get; }

        public bool IsMatch(string rootPath)
        {
            foreach (var directoryMarker in DirectoryMarkers)
            {
                if (Directory.Exists(Path.Combine(rootPath, directoryMarker)))
                {
                    return true;
                }
            }

            foreach (var fileMarker in FileMarkers)
            {
                if (File.Exists(Path.Combine(rootPath, fileMarker)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

public sealed class AntiCheatCatalogEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("anticheats")]
    public List<string> Anticheats { get; set; } = new();

    [JsonPropertyName("storeIds")]
    public AntiCheatCatalogStoreIds? StoreIds { get; set; }
}

public sealed class AntiCheatCatalogStoreIds
{
    [JsonPropertyName("steam")]
    public string? Steam { get; set; }
}
