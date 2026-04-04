using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ValveKeyValue;

namespace Optinstaller.Services;

public sealed class SteamAppInfoService
{
    private static readonly string[] AllowedAppTypes =
    {
        "Game", "Demo", "Mod"
    };

    private static readonly string[] IgnoredLaunchTokens =
    {
        "launcher", "config", "tool", "tools", "editor", "workshop", "viewer", "benchmark", "server",
        "dedicated", "sdk", "mod", "legacy", "test", "safe mode"
    };

    public SteamAppInfoIndex Load()
    {
        var appInfoPath = GetAppInfoPath();
        if (string.IsNullOrWhiteSpace(appInfoPath) || !File.Exists(appInfoPath))
        {
            return SteamAppInfoIndex.Empty;
        }

        try
        {
            using var stream = File.Open(appInfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }
        catch
        {
            return SteamAppInfoIndex.Empty;
        }
    }

    public SteamAppInfoIndex Parse(Stream input)
    {
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        var version = (int)(magic & 0xFF);
        magic >>= 8;
        if (magic != 0x07_56_44 || version < 39 || version > 41)
        {
            throw new InvalidDataException("Unsupported appinfo.vdf format.");
        }

        _ = reader.ReadUInt32();

        var options = new KVSerializerOptions();
        if (version >= 41)
        {
            var stringTableOffset = reader.ReadInt64();
            var currentOffset = reader.BaseStream.Position;

            reader.BaseStream.Position = stringTableOffset;
            var stringCount = reader.ReadUInt32();
            var stringPool = new string[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                stringPool[i] = ReadNullTermUtf8String(reader.BaseStream);
            }

            reader.BaseStream.Position = currentOffset;
            options.StringTable = new StringTable(stringPool);
        }

        var deserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary);
        var entries = new Dictionary<string, SteamAppInfoEntry>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var appId = reader.ReadUInt32();
            if (appId == 0)
            {
                break;
            }

            var size = reader.ReadUInt32();
            var endOffset = reader.BaseStream.Position + size;

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt64();
            _ = reader.ReadBytes(20);
            _ = reader.ReadUInt32();

            if (version >= 40)
            {
                _ = reader.ReadBytes(20);
            }

            var data = deserializer.Deserialize(input, options);
            if (reader.BaseStream.Position != endOffset)
            {
                reader.BaseStream.Position = endOffset;
            }

            var entry = CreateEntry(appId, data);
            if (entry != null)
            {
                entries[entry.AppId] = entry;
            }
        }

        return new SteamAppInfoIndex(entries);
    }

    private static SteamAppInfoEntry? CreateEntry(uint appId, KVObject data)
    {
        var common = TryGetChild(data, "common");
        var config = TryGetChild(data, "config");

        var appType = AsString(TryGetChild(common, "type"));
        var launch = ResolvePreferredWindowsLaunch(TryGetChild(config, "launch"));

        return new SteamAppInfoEntry(
            appId.ToString(),
            appType ?? string.Empty,
            launch?.Executable,
            launch?.Description,
            launch?.OptionKey);
    }

    private static SteamLaunchOption? ResolvePreferredWindowsLaunch(KVObject? launchCollection)
    {
        if (launchCollection == null)
        {
            return null;
        }

        return launchCollection.Children
            .Select(pair => ToLaunchOption(pair.Name ?? string.Empty, pair))
            .Where(option => option != null && option.IsWindows && IsSupportedLaunchExecutable(option.Executable))
            .OrderByDescending(option => GetLaunchScore(option!))
            .FirstOrDefault();
    }

    private static SteamLaunchOption? ToLaunchOption(string optionKey, KVObject launchData)
    {
        var executable = NormalizeLaunchExecutable(AsString(launchData["executable"]));
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        var description = FirstNonEmpty(
            AsString(TryGetChild(launchData, "description")),
            AsString(TryGetChild(launchData, "description_loc")),
            AsString(TryGetChild(launchData, "name")));

        var config = TryGetChild(launchData, "config");
        var osList = FirstNonEmpty(
            AsString(TryGetChild(config, "oslist")),
            AsString(TryGetChild(launchData, "oslist")));

        return new SteamLaunchOption(
            optionKey,
            executable,
            description ?? string.Empty,
            osList ?? string.Empty);
    }

    private static bool IsSupportedLaunchExecutable(string executable)
    {
        return executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLaunchExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return string.Empty;
        }

        return executable.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static int GetLaunchScore(SteamLaunchOption option)
    {
        var score = 0;

        if (string.Equals(option.OptionKey, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.OptionKey, "default", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        if (string.IsNullOrWhiteSpace(option.Description))
        {
            score += 250;
        }

        var lowerDescription = option.Description.ToLowerInvariant();
        if (lowerDescription.Contains("default", StringComparison.OrdinalIgnoreCase) ||
            lowerDescription.Contains("play", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        var lowerExecutable = option.Executable.ToLowerInvariant();
        if (!IgnoredLaunchTokens.Any(token => lowerExecutable.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                                              lowerDescription.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            score += 300;
        }

        if (lowerExecutable.Contains("win64", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (lowerExecutable.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
            lowerExecutable.StartsWith("game", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        score -= lowerExecutable.Count(ch => ch == Path.DirectorySeparatorChar) * 5;
        return score;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? AsString(KVValue? value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            return value.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? AsString(KVObject? value)
    {
        return value == null ? null : AsString(value.Value);
    }

    private static KVObject? TryGetChild(KVObject? parent, string key)
    {
        if (parent == null)
        {
            return null;
        }

        return parent.Children.FirstOrDefault(child => child.Name.Equals(key, StringComparison.Ordinal));
    }

    private static string ReadNullTermUtf8String(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64);
        try
        {
            var position = 0;
            while (true)
            {
                var value = stream.ReadByte();
                if (value <= 0)
                {
                    break;
                }

                if (position >= buffer.Length)
                {
                    var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuffer;
                }

                buffer[position++] = (byte)value;
            }

            return System.Text.Encoding.UTF8.GetString(buffer, 0, position);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string? GetAppInfoPath()
    {
        var steamPath = TryGetSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return null;
        }

        var appInfoPath = Path.Combine(steamPath, "appcache", "appinfo.vdf");
        return File.Exists(appInfoPath) ? appInfoPath : null;
    }

    private static string? TryGetSteamPath()
    {
        var candidates = new[]
        {
            Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam")?.GetValue("SteamPath") as string,
            Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam")?.GetValue("InstallPath") as string,
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\Valve\\Steam")?.GetValue("InstallPath") as string,
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey("SOFTWARE\\Valve\\Steam")?.GetValue("InstallPath") as string,
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static bool IsSupportedAppType(string? appType)
    {
        if (string.IsNullOrWhiteSpace(appType))
        {
            return true;
        }

        return AllowedAppTypes.Any(value => value.Equals(appType, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ResolveExecutablePath(string installRootPath, SteamAppInfoEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.PreferredExecutable))
        {
            return null;
        }

        var resolvedPath = entry.PreferredExecutable.Replace("%INSTALLDIR%", installRootPath, StringComparison.OrdinalIgnoreCase);
        if (!Path.IsPathRooted(resolvedPath))
        {
            resolvedPath = Path.Combine(installRootPath, resolvedPath);
        }

        try
        {
            var fullPath = Path.GetFullPath(resolvedPath);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class SteamAppInfoIndex
{
    public static SteamAppInfoIndex Empty { get; } = new(new Dictionary<string, SteamAppInfoEntry>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, SteamAppInfoEntry> _entries;

    public SteamAppInfoIndex(IReadOnlyDictionary<string, SteamAppInfoEntry> entries)
    {
        _entries = entries;
    }

    public SteamAppInfoEntry? Get(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        return _entries.TryGetValue(appId, out var entry) ? entry : null;
    }
}

public sealed record SteamAppInfoEntry(string AppId, string AppType, string? PreferredExecutable, string? LaunchDescription, string? LaunchOptionKey);

internal sealed record SteamLaunchOption(string OptionKey, string Executable, string Description, string OsList)
{
    public bool IsWindows => string.IsNullOrWhiteSpace(OsList) ||
                             OsList.Contains("windows", StringComparison.OrdinalIgnoreCase);
}
