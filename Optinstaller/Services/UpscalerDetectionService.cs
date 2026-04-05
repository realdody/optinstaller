using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Optinstaller.Models;

namespace Optinstaller.Services;

public sealed class UpscalerDetectionService
{
    private const int MaxSearchDepth = 7;

    private static readonly string[] StructuralDirectoryNames =
    {
        "Binaries", "Binary", "Bin", "Win64", "Win32", "WinGDK", "x64", "x86", "Release", "Retail", "Debug"
    };

    private static readonly string[] IgnoredDirectoryNames =
    {
        "_CommonRedist", "CommonRedist", "DirectXRedist", "Redistributables", "Redist", "Support", "Launcher",
        "Launchers", "CrashReportClient", "EasyAntiCheat", "BattlEye", "Content", "Paks", "Saved", "Logs",
        "Movies", "Screenshots", "Captures", "Localization", "ShaderCache", "DerivedDataCache", "Telemetry",
        "Dumps", "CrashDumps", "Cache", "Temp", "Tmp", "Docs", "Doc", "Manual", "Manuals"
    };

    private static readonly string[] PrioritizedDirectoryNames =
    {
        "Plugins", "Plugin", "Engine", "Binaries", "Binary", "Bin", "ThirdParty", "Win64", "WinGDK", "x64",
        "NVIDIA", "AMD", "XeSS", "DLSS", "XeSS2"
    };

    private static readonly UpscalerComponentDefinition[] ComponentDefinitions =
    {
        new("DLSS", new[] { "nvngx_dlss.dll", "sl.dlss.dll" }),
        new("DLSS FG", new[] { "nvngx_dlssg.dll", "sl.dlss_g.dll" }),
        new("FSR Upscaler", new[] { "amd_fidelityfx_upscaler_dx12.dll" }),
        new("FSR FG", new[] { "amd_fidelityfx_framegeneration_dx12.dll" }),
        new("XeSS", new[] { "libxess.dll" }),
        new("XeFG", new[] { "libxess_fg.dll" }),
        new("Streamline", new[] { "sl.interposer.dll", "sl.common.dll", "sl.reflex.dll" }),
    };

    private static readonly Dictionary<string, UpscalerComponentDefinition> DefinitionsByFileName = ComponentDefinitions
        .SelectMany(definition => definition.FileNames.Select(fileName => new KeyValuePair<string, UpscalerComponentDefinition>(fileName, definition)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, UpscalerDetectionResult> _cachedResults = new(StringComparer.OrdinalIgnoreCase);

    public UpscalerDetectionResult Detect(string gamePath, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return new UpscalerDetectionResult(string.Empty, CreateMissingComponentEntries(), false, "No supported upscalers detected");
        }

        var searchRootPath = ResolveSearchRoot(gamePath);
        if (!forceRefresh)
        {
            lock (_cacheLock)
            {
                if (_cachedResults.TryGetValue(searchRootPath, out var cachedResult))
                {
                    return cachedResult;
                }
            }
        }

        var result = DetectCore(searchRootPath);
        lock (_cacheLock)
        {
            _cachedResults[searchRootPath] = result;
        }

        return result;
    }

    public void Invalidate(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return;
        }

        var searchRootPath = ResolveSearchRoot(gamePath);
        lock (_cacheLock)
        {
            _cachedResults.Remove(searchRootPath);
        }
    }

    private static UpscalerDetectionResult DetectCore(string searchRootPath)
    {
        var detectedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<(string Path, int Depth)>();
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pendingDirectories.Push((searchRootPath, 0));

        while (pendingDirectories.Count > 0)
        {
            var (currentDirectory, depth) = pendingDirectories.Pop();
            if (!visitedDirectories.Add(currentDirectory))
            {
                continue;
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(currentDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (string.IsNullOrWhiteSpace(fileName) || !DefinitionsByFileName.TryGetValue(fileName, out var definition))
                    {
                        continue;
                    }

                    if (!detectedPaths.ContainsKey(definition.Label))
                    {
                        detectedPaths[definition.Label] = Path.GetFullPath(filePath);
                    }
                }
            }
            catch
            {
            }

            if (depth >= MaxSearchDepth || detectedPaths.Count == ComponentDefinitions.Length)
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
                         .Where(ShouldSearchDirectory)
                         .OrderByDescending(path => GetDirectoryPriority(Path.GetFileName(path))))
            {
                pendingDirectories.Push((childDirectory, depth + 1));
            }
        }

        var components = new List<UpscalerComponentDetection>(ComponentDefinitions.Length);
        var detectedLabels = new List<string>();
        foreach (var definition in ComponentDefinitions)
        {
            if (detectedPaths.TryGetValue(definition.Label, out var detectedPath) && File.Exists(detectedPath))
            {
                var detectedFileName = Path.GetFileName(detectedPath);
                components.Add(new UpscalerComponentDetection(
                    definition.Label,
                    detectedFileName,
                    GetDetectedDllVersion(detectedPath),
                    true,
                    Path.GetRelativePath(searchRootPath, detectedPath)));
                detectedLabels.Add(definition.Label);
                continue;
            }

            components.Add(new UpscalerComponentDetection(definition.Label, definition.DefaultFileName, "Not detected", false, string.Empty));
        }

        var hasSupportedComponents = detectedLabels.Count > 0;
        var summary = hasSupportedComponents
            ? string.Join(", ", detectedLabels)
            : "No supported upscalers detected";
        return new UpscalerDetectionResult(searchRootPath, components, hasSupportedComponents, summary);
    }

    private static IReadOnlyList<UpscalerComponentDetection> CreateMissingComponentEntries()
    {
        return ComponentDefinitions
            .Select(definition => new UpscalerComponentDetection(definition.Label, definition.DefaultFileName, "Not detected", false, string.Empty))
            .ToList();
    }

    private static bool ShouldSearchDirectory(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath);
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        return !IgnoredDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetDirectoryPriority(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return 0;
        }

        return PrioritizedDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase))
            ? 2
            : 1;
    }

    private static string ResolveSearchRoot(string gamePath)
    {
        var currentDirectory = new DirectoryInfo(Path.GetFullPath(gamePath));
        while (currentDirectory.Parent != null && IsStructuralDirectory(currentDirectory.Name))
        {
            currentDirectory = currentDirectory.Parent;
        }

        return currentDirectory.FullName;
    }

    private static bool IsStructuralDirectory(string directoryName)
    {
        return StructuralDirectoryNames.Any(name => name.Equals(directoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDetectedDllVersion(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var productVersion = NormalizeVersion(versionInfo.ProductVersion, trimTrailingBuildSegment: false);
            if (!string.IsNullOrWhiteSpace(productVersion))
            {
                return productVersion;
            }

            var fileVersion = NormalizeVersion(versionInfo.FileVersion, trimTrailingBuildSegment: true);
            return string.IsNullOrWhiteSpace(fileVersion) ? "Unknown" : fileVersion;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string NormalizeVersion(string? rawVersion, bool trimTrailingBuildSegment)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return string.Empty;
        }

        var version = rawVersion.Trim();
        if (trimTrailingBuildSegment && version.EndsWith(".0", StringComparison.Ordinal) && version.Split('.').Length >= 4)
        {
            version = version[..^2];
        }

        return version.Trim();
    }

    private sealed record UpscalerComponentDefinition(string Label, IReadOnlyList<string> FileNames)
    {
        public string DefaultFileName => FileNames[0];
    }
}
