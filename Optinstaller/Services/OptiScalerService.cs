using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Text.RegularExpressions;
using Optinstaller.Models;

namespace Optinstaller.Services;

public class OptiScalerService
{
    private const string OptiScalerLogName = "OptiScaler.log";
    private const string OptiScalerIniName = "OptiScaler.ini";
    private const string OptiPatcherUrl = "https://raw.githubusercontent.com/optiscaler/OptiPatcher/main/OptiPatcher/dllmain.cpp";
    private const string OptiPatcherDownloadUrl = "https://github.com/optiscaler/OptiPatcher/releases/download/rolling/OptiPatcher.asi";
    private const string OptiPatcherRelativePath = "plugins\\OptiPatcher.asi";
    private const string FsrUpscalerDllName = "amd_fidelityfx_upscaler_dx12.dll";
    private const string SpecialKDxgiMarkerName = "SpecialK.dxgi";
    private const string ManagedLoaderInstallStateName = "Optinstaller.ManagedLoader.json";

    private static readonly string[] PossibleFilenames = 
    { 
        "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", 
        "d3d12.dll", "wininet.dll", "winhttp.dll", "OptiScaler.asi" 
    };

    private static readonly string[] PreferredAlternativeFilenames =
    {
        "winmm.dll", "version.dll", "dbghelp.dll", "wininet.dll", "winhttp.dll", "d3d12.dll"
    };

    private static readonly string[] AsiLoaderProbeFilenames =
    {
        "dxgi.dll", "opengl32.dll", "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll",
        "dinput.dll", "dinput8.dll", "dsound.dll", "msacm32.dll", "msvfw32.dll", "version.dll", "wininet.dll",
        "winmm.dll", "winhttp.dll", "xlive.dll", "binkw32.dll", "bink2w32.dll", "binkw64.dll", "bink2w64.dll",
        "vorbisFile.dll", "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "xinputuap.dll"
    };

    private static readonly string[] SpecialKProbeFilenames =
    {
        "dxgi.dll", "d3d11.dll", "d3d9.dll", "opengl32.dll", "d3d8.dll", "ddraw.dll", "dinput8.dll"
    };

    public bool IsInstalled(string gamePath, out string installedFilename, out string detectedVersion, out string fsrVersion, out bool isOptiPatcherInstalled)
    {
        installedFilename = string.Empty;
        detectedVersion = string.Empty;
        fsrVersion = string.Empty;
        isOptiPatcherInstalled = false;
        
        if (!File.Exists(Path.Combine(gamePath, OptiScalerIniName)))
            return false;

        fsrVersion = DetectFsrVersion(gamePath);
        isOptiPatcherInstalled = File.Exists(Path.Combine(gamePath, OptiPatcherRelativePath));

        foreach (var file in PossibleFilenames)
        {
            var path = Path.Combine(gamePath, file);
            if (File.Exists(path))
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if ((info.ProductName?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true) ||
                        (info.FileDescription?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true) ||
                        (info.CompanyName?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        installedFilename = file;
                        detectedVersion = GetVersionFromFileInfo(info);
                        return true;
                    }
                }
                catch
                {
                }
            }
        }
        
        return false;
    }

    public InstallTargetConflictInfo AnalyzeTargetFilename(string gamePath, string targetFilename)
    {
        if (string.IsNullOrWhiteSpace(gamePath) ||
            string.IsNullOrWhiteSpace(targetFilename) ||
            !Directory.Exists(gamePath))
        {
            return InstallTargetConflictInfo.None;
        }

        var existingModule = IdentifyExistingModule(gamePath, targetFilename);
        var managedChain = BuildManagedChainRecommendation(gamePath, targetFilename, existingModule);
        var asiLoader = existingModule.IsAsiLoader ? existingModule : DetectAsiLoaderIdentity(gamePath);
        return new InstallTargetConflictInfo
        {
            TargetFilename = targetFilename,
            FileExists = !string.IsNullOrWhiteSpace(existingModule.ProviderName),
            IsOptiScaler = existingModule.IsOptiScaler,
            ExistingProvider = existingModule.ProviderName,
            ExistingDetails = existingModule.Details,
            RecommendedFilename = GetRecommendedTargetFilename(gamePath, targetFilename, existingModule, asiLoader, managedChain),
            AsiLoaderProvider = asiLoader.ProviderName,
            AsiLoaderInstructions = asiLoader.AsiInstructions,
            ChainedLoaderProvider = managedChain.ProviderName,
            ChainedLoaderSourceFilename = managedChain.SourceFilename,
            ChainedLoaderDestinationFilename = managedChain.RedirectedFilename,
            ChainedLoaderInstructions = managedChain.Instructions,
        };
    }

    public bool IsSupportedExecutableArchitecture(string executablePath)
    {
        return TryIsPortableExecutable64Bit(executablePath) == true;
    }

    // Overload for backward compatibility
    public bool IsInstalled(string gamePath, out string installedFilename, out string detectedVersion)
    {
        return IsInstalled(gamePath, out installedFilename, out detectedVersion, out _, out _);
    }

    public bool IsInstalled(string gamePath, out string installedFilename)
    {
        return IsInstalled(gamePath, out installedFilename, out _);
    }

    private static string GetVersionFromFileInfo(FileVersionInfo info)
    {
        var productVersion = NormalizeVersionString(info.ProductVersion, trimTrailingBuildSegment: false);
        if (!string.IsNullOrWhiteSpace(productVersion))
        {
            return productVersion;
        }

        var fileVersion = NormalizeVersionString(info.FileVersion, trimTrailingBuildSegment: true);
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        return "Unknown";
    }

    private static string NormalizeVersionString(string? rawVersion, bool trimTrailingBuildSegment)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return string.Empty;
        }

        var version = rawVersion.Trim();
        version = Regex.Replace(version, @"\s+\(([0-9a-f]{7,40})\)$", string.Empty, RegexOptions.IgnoreCase);

        if (trimTrailingBuildSegment && version.EndsWith(".0", StringComparison.Ordinal) && version.Count(c => c == '.') >= 3)
        {
            version = version[..^2];
        }

        return version.Trim();
    }

    private static string DetectFsrVersion(string gamePath)
    {
        var path = Path.Combine(gamePath, FsrUpscalerDllName);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var fileInfo = new FileInfo(path);
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            if (IsKnownFsr4Int8Binary(fileInfo.Length, versionInfo.FileVersion))
            {
                return "4 Int8";
            }

            var fileVersion = NormalizeVersionString(versionInfo.FileVersion, trimTrailingBuildSegment: true);
            return string.IsNullOrWhiteSpace(fileVersion) ? "Unknown" : fileVersion;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static bool IsKnownFsr4Int8Binary(long fileLength, string? rawFileVersion)
    {
        // This size floor is based on the FSR 4.0.2 Int8 DLL used as the current reference build.
        if (fileLength < 30_000_000)
        {
            return false;
        }

        return string.Equals(rawFileVersion?.Trim(), "4.0.2.0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(NormalizeVersionString(rawFileVersion, trimTrailingBuildSegment: true), "4.0.2", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InstallAsync(InstallationOptions options)
    {
        await Task.Run(async () => 
        {
            var gamePath = options.GamePath;
            var versionPath = options.VersionPath;
            var targetFilename = options.TargetFilename;

            var managedLoaderState = PrepareManagedChainedLoader(options);
            if (managedLoaderState != null)
            {
                SaveManagedLoaderInstallState(gamePath, managedLoaderState);
            }

            // Use the shared method to copy the DLL
            UpdateDll(gamePath, versionPath, targetFilename);

            var configPath = Path.Combine(gamePath, OptiScalerIniName);
            var sourceConfig = Path.Combine(versionPath, OptiScalerIniName);
            
            if (!File.Exists(configPath) && File.Exists(sourceConfig))
            {
                File.Copy(sourceConfig, configPath);
            }

            if (File.Exists(configPath))
            {
                var content = File.ReadAllText(configPath);
                
                // If not enabled (AMD/Intel), force false. If enabled (Nvidia), we generally leave as auto or default.
                if (!options.EnableSpoofing)
                {
                    content = content.Replace("Dxgi=auto", "Dxgi=false")
                                     .Replace("Dxgi=true", "Dxgi=false");
                }

                if (string.Equals(options.ChainedLoaderProvider, "ReShade", StringComparison.OrdinalIgnoreCase))
                {
                    content = SetOrReplaceConfigValue(content, "LoadReshade", "true");
                }

                if (string.Equals(options.ChainedLoaderProvider, "Special K", StringComparison.OrdinalIgnoreCase))
                {
                    content = SetOrReplaceConfigValue(content, "LoadSpecialK", "true");
                }
                 
                if (options.UseOptiPatcher)
                {
                     content = content.Replace("LoadAsiPlugins=auto", "LoadAsiPlugins=true");
                     content = content.Replace("LoadAsiPlugins=false", "LoadAsiPlugins=true");
                }

                File.WriteAllText(configPath, content);
            }

            if (options.UseOptiPatcher)
            {
                var pluginsDir = Path.Combine(gamePath, "plugins");
                if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);

                var patcherDest = Path.Combine(gamePath, OptiPatcherRelativePath);
                
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Optinstaller");
                var data = await client.GetByteArrayAsync(OptiPatcherDownloadUrl);
                await File.WriteAllBytesAsync(patcherDest, data);
            }

            if (options.CreateUninstaller)
            {
                 CreateUninstallerBat(gamePath, targetFilename);
            }
        });
    }

    public void UpdateDll(string gamePath, string versionPath, string targetFilename)
    {
        var sourceDll = Path.Combine(versionPath, "OptiScaler.dll");
        
        if (!File.Exists(sourceDll))
            throw new FileNotFoundException($"OptiScaler.dll not found in {versionPath}.");

        var dest = Path.Combine(gamePath, targetFilename);

        // Atomic replace: copy to temp file first, then move/overwrite
        var tempDest = dest + ".tmp";
        File.Copy(sourceDll, tempDest, true);
        File.Move(tempDest, dest, true);
    }

    private void CreateUninstallerBat(string gamePath, string filename)
    {
        var batPath = Path.Combine(gamePath, "Remove OptiScaler.bat");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("cls");
        sb.AppendLine("echo OptiScaler Uninstaller");
        sb.AppendLine("echo.");
        sb.AppendLine("set /p removeChoice=\"Do you want to remove OptiScaler? [y/n]: \"");
        sb.AppendLine("if /i \"%removeChoice%\"==\"y\" (");
        sb.AppendLine($"    if exist \"{OptiScalerLogName}\" del \"{OptiScalerLogName}\"");
        sb.AppendLine($"    if exist \"{OptiScalerIniName}\" del \"{OptiScalerIniName}\"");
        sb.AppendLine($"    if exist \"{filename}\" del \"{filename}\"");
        sb.AppendLine("    if exist \"fakenvapi.dll\" del \"fakenvapi.dll\"");
        sb.AppendLine("    if exist \"fakenvapi.ini\" del \"fakenvapi.ini\"");
        sb.AppendLine("    if exist \"fakenvapi.log\" del \"fakenvapi.log\"");
        sb.AppendLine("    if exist \"dlssg_to_fsr3_amd_is_better.dll\" del \"dlssg_to_fsr3_amd_is_better.dll\"");
        sb.AppendLine("    if exist \"dlssg_to_fsr3.log\" del \"dlssg_to_fsr3.log\"");
        sb.AppendLine($"    if exist \"{OptiPatcherRelativePath}\" del \"{OptiPatcherRelativePath}\"");
        sb.AppendLine($"    if exist \"{ManagedLoaderInstallStateName}\" powershell -NoProfile -Command \"$state = Get-Content -LiteralPath '{ManagedLoaderInstallStateName}' | ConvertFrom-Json; if (-not (Test-Path -LiteralPath $state.OriginalFilename) -and (Test-Path -LiteralPath $state.RedirectedFilename)) {{ Move-Item -LiteralPath $state.RedirectedFilename -Destination $state.OriginalFilename }}; if ($state.CreatedSpecialKDxgiMarker -and (Test-Path -LiteralPath '{SpecialKDxgiMarkerName}')) {{ Remove-Item -LiteralPath '{SpecialKDxgiMarkerName}' -Force }}; Remove-Item -LiteralPath '{ManagedLoaderInstallStateName}' -Force\"");
        sb.AppendLine("    if exist \"plugins\" rmdir \"plugins\"");
        sb.AppendLine("    if exist \"D3D12_Optiscaler\" rmdir /s /q \"D3D12_Optiscaler\"");
        sb.AppendLine("    if exist \"DlssOverrides\" rmdir /s /q \"DlssOverrides\"");
        sb.AppendLine("    if exist \"Licenses\" rmdir /s /q \"Licenses\"");
        sb.AppendLine("    echo OptiScaler removed!");
        sb.AppendLine("    pause");
        sb.AppendLine("    del %0");
        sb.AppendLine(")");
        
        File.WriteAllText(batPath, sb.ToString());
    }

    public async Task<bool> CheckOptiPatcherSupportAsync(string gamePath)
    {
        try
        {
            using var client = new HttpClient();
            var code = await client.GetStringAsync(OptiPatcherUrl);

            var exes = Directory.GetFiles(gamePath, "*.exe").Select(Path.GetFileName).ToList();
            if (!exes.Any()) return false;

            // Match CHECK_UE(Name) -> Name-win64-shipping.exe
            var ueMatches = Regex.Matches(code, @"CHECK_UE\s*\(\s*([a-zA-Z0-9_]+)\s*\)");
            foreach (Match match in ueMatches)
            {
                if (match.Groups.Count > 1)
                {
                    var baseName = match.Groups[1].Value;
                    var win64 = $"{baseName}-win64-shipping.exe";
                    var wingdk = $"{baseName}-wingdk-shipping.exe";
                    
                    if (exes.Any(e => (e ?? "").Equals(win64, StringComparison.OrdinalIgnoreCase) || 
                                      (e ?? "").Equals(wingdk, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            // Simple approximation of the C++ logic for direct exe matches
            var directMatches = Regex.Matches(code, @"exeName\s*==\s*[\x22\x27]([^\x22\x27]+)[\x22\x27]");
            foreach (Match match in directMatches)
            {
                 if (match.Groups.Count > 1)
                 {
                     var name = match.Groups[1].Value;
                     if (exes.Any(e => (e ?? "").Equals(name, StringComparison.OrdinalIgnoreCase)))
                     {
                         return true;
                     }
                 }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task UninstallAsync(string gamePath, string installedFilename)
    {
        await Task.Run(() =>
        {
            var managedLoaderState = LoadManagedLoaderInstallState(gamePath);

            var filesToRemove = new List<string>
            {
                OptiScalerLogName,
                OptiScalerIniName,
                installedFilename,
                "fakenvapi.dll",
                "fakenvapi.ini",
                "fakenvapi.log",
                "dlssg_to_fsr3_amd_is_better.dll",
                "dlssg_to_fsr3.log",
                "Remove OptiScaler.bat"
            };

            foreach (var file in filesToRemove)
            {
                var path = Path.Combine(gamePath, file);
                if (File.Exists(path)) File.Delete(path);
            }
            
            var patcher = Path.Combine(gamePath, OptiPatcherRelativePath);
            if (File.Exists(patcher)) File.Delete(patcher);
            
            var pluginsDir = Path.Combine(gamePath, "plugins");
            if (Directory.Exists(pluginsDir) && !Directory.EnumerateFileSystemEntries(pluginsDir).Any())
                Directory.Delete(pluginsDir);

            var dirsToRemove = new[] { "D3D12_Optiscaler", "DlssOverrides", "Licenses" };
            foreach (var dir in dirsToRemove)
            {
                var path = Path.Combine(gamePath, dir);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }

            RestoreManagedChainedLoader(gamePath, managedLoaderState);
        });
    }

    private static ManagedLoaderInstallState? PrepareManagedChainedLoader(InstallationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ChainedLoaderProvider) ||
            string.IsNullOrWhiteSpace(options.ChainedLoaderSourceFilename) ||
            string.IsNullOrWhiteSpace(options.ChainedLoaderDestinationFilename))
        {
            return null;
        }

        var sourcePath = Path.Combine(options.GamePath, options.ChainedLoaderSourceFilename);
        var redirectedPath = Path.Combine(options.GamePath, options.ChainedLoaderDestinationFilename);

        if (File.Exists(sourcePath) && !sourcePath.Equals(redirectedPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(redirectedPath))
            {
                throw new InvalidOperationException($"Cannot prepare {options.ChainedLoaderProvider}: {options.ChainedLoaderDestinationFilename} already exists.");
            }

            File.Move(sourcePath, redirectedPath);
        }

        var createdMarker = false;
        if (options.CreateSpecialKDxgiMarker)
        {
            var markerPath = Path.Combine(options.GamePath, SpecialKDxgiMarkerName);
            if (!File.Exists(markerPath))
            {
                File.WriteAllText(markerPath, string.Empty);
                createdMarker = true;
            }
        }

        return new ManagedLoaderInstallState
        {
            Provider = options.ChainedLoaderProvider,
            OriginalFilename = options.ChainedLoaderSourceFilename,
            RedirectedFilename = options.ChainedLoaderDestinationFilename,
            CreatedSpecialKDxgiMarker = createdMarker,
        };
    }

    private static void SaveManagedLoaderInstallState(string gamePath, ManagedLoaderInstallState state)
    {
        var statePath = Path.Combine(gamePath, ManagedLoaderInstallStateName);
        var json = JsonSerializer.Serialize(state, OptiScalerServiceJsonContext.Default.ManagedLoaderInstallState);
        File.WriteAllText(statePath, json);
    }

    private static ManagedLoaderInstallState? LoadManagedLoaderInstallState(string gamePath)
    {
        var statePath = Path.Combine(gamePath, ManagedLoaderInstallStateName);
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize(json, OptiScalerServiceJsonContext.Default.ManagedLoaderInstallState);
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreManagedChainedLoader(string gamePath, ManagedLoaderInstallState? state)
    {
        var statePath = Path.Combine(gamePath, ManagedLoaderInstallStateName);
        try
        {
            if (state == null)
            {
                if (File.Exists(statePath))
                {
                    File.Delete(statePath);
                }

                return;
            }

            var redirectedPath = Path.Combine(gamePath, state.RedirectedFilename);
            var originalPath = Path.Combine(gamePath, state.OriginalFilename);
            if (File.Exists(redirectedPath) && !File.Exists(originalPath))
            {
                File.Move(redirectedPath, originalPath);
            }

            if (state.CreatedSpecialKDxgiMarker)
            {
                var markerPath = Path.Combine(gamePath, SpecialKDxgiMarkerName);
                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }
            }
        }
        finally
        {
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }
        }
    }

    private static string SetOrReplaceConfigValue(string content, string key, string value)
    {
        var pattern = $@"(?im)^\s*{Regex.Escape(key)}\s*=.*$";
        if (Regex.IsMatch(content, pattern))
        {
            return Regex.Replace(content, pattern, $"{key}={value}");
        }

        var separator = content.EndsWith(Environment.NewLine, StringComparison.Ordinal) || string.IsNullOrEmpty(content)
            ? string.Empty
            : Environment.NewLine;
        return content + separator + $"{key}={value}" + Environment.NewLine;
    }

    private static ManagedChainRecommendation BuildManagedChainRecommendation(string gamePath, string targetFilename, ExistingModuleIdentity existingModule)
    {
        if (!targetFilename.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
        {
            return ManagedChainRecommendation.None;
        }

        if (existingModule.IsOptiScaler)
        {
            return ManagedChainRecommendation.None;
        }

        if (existingModule.CanBeChainedThroughOptiScaler)
        {
            return CreateManagedChainRecommendation(gamePath, existingModule);
        }

        if (!string.IsNullOrWhiteSpace(existingModule.ProviderName))
        {
            return ManagedChainRecommendation.None;
        }

        var specialK = DetectSpecialKIdentity(gamePath);
        return specialK.CanBeChainedThroughOptiScaler
            ? CreateManagedChainRecommendation(gamePath, specialK)
            : ManagedChainRecommendation.None;
    }

    private static ManagedChainRecommendation CreateManagedChainRecommendation(string gamePath, ExistingModuleIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.SourceFilename))
        {
            return ManagedChainRecommendation.None;
        }

        var sourcePath = Path.Combine(gamePath, identity.SourceFilename);
        if (!File.Exists(sourcePath))
        {
            return ManagedChainRecommendation.None;
        }

        var redirectedFilename = GetManagedChainDestinationFilename(identity.ProviderName, sourcePath);
        if (string.IsNullOrWhiteSpace(redirectedFilename))
        {
            return ManagedChainRecommendation.None;
        }

        if (!redirectedFilename.Equals(identity.SourceFilename, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(gamePath, redirectedFilename)))
        {
            return ManagedChainRecommendation.None;
        }

        return new ManagedChainRecommendation(
            identity.ProviderName,
            identity.SourceFilename,
            redirectedFilename,
            BuildManagedChainInstructions(identity.ProviderName, redirectedFilename),
            identity.ProviderName.Equals("Special K", StringComparison.OrdinalIgnoreCase));
    }

    private static ExistingModuleIdentity IdentifyExistingModule(string gamePath, string targetFilename)
    {
        var path = Path.Combine(gamePath, targetFilename);
        if (!File.Exists(path))
        {
            return ExistingModuleIdentity.None;
        }

        var metadata = ReadMetadataText(path, out var details);
        if (ContainsAny(metadata, "optiscaler"))
        {
            return new ExistingModuleIdentity(targetFilename, "OptiScaler", details, IsOptiScaler: true, IsAsiLoader: false, CanBeChainedThroughOptiScaler: false);
        }

        if (ContainsAny(metadata, "reshade") ||
            File.Exists(Path.Combine(gamePath, "ReShade.ini")) ||
            File.Exists(Path.Combine(gamePath, "ReShade.log")) ||
            Directory.Exists(Path.Combine(gamePath, "reshade-shaders")))
        {
            var canChain = targetFilename.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase);
            return new ExistingModuleIdentity(targetFilename, "ReShade", details, IsOptiScaler: false, IsAsiLoader: false, CanBeChainedThroughOptiScaler: canChain);
        }

        if (ContainsAny(metadata, "dxvk", "vkd3d") ||
            File.Exists(Path.Combine(gamePath, "dxvk.conf")))
        {
            return new ExistingModuleIdentity(targetFilename, "DXVK", details, IsOptiScaler: false, IsAsiLoader: false, CanBeChainedThroughOptiScaler: false);
        }

        if (ContainsAny(metadata, "special k", "specialk") ||
            File.Exists(Path.Combine(gamePath, "SpecialK.ini")) ||
            File.Exists(Path.Combine(gamePath, "SpecialK.log")) ||
            File.Exists(Path.Combine(gamePath, "SpecialK.central")))
        {
            return new ExistingModuleIdentity(targetFilename, "Special K", details, IsOptiScaler: false, IsAsiLoader: false, CanBeChainedThroughOptiScaler: true);
        }

        if (ContainsAny(metadata, "ultimate asi loader", "universal asi loader", "asi loader", "ultimate asi") ||
            (Directory.Exists(Path.Combine(gamePath, "scripts")) && Path.GetFileName(path).Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase)))
        {
            return new ExistingModuleIdentity(
                targetFilename,
                "Ultimate ASI Loader",
                details,
                IsOptiScaler: false,
                IsAsiLoader: true,
                CanBeChainedThroughOptiScaler: false,
                AsiInstructions: "Keep the current loader in place and install OptiScaler.asi in the game folder. Ultimate ASI Loader should pick it up automatically.");
        }

        if (ContainsAny(metadata, "enbseries", "enb") ||
            File.Exists(Path.Combine(gamePath, "enbseries.ini")))
        {
            return new ExistingModuleIdentity(targetFilename, "ENBSeries", details, IsOptiScaler: false, IsAsiLoader: false, CanBeChainedThroughOptiScaler: false);
        }

        return new ExistingModuleIdentity(targetFilename, "another proxy DLL or mod loader", details, IsOptiScaler: false, IsAsiLoader: false, CanBeChainedThroughOptiScaler: false);
    }

    private static ExistingModuleIdentity DetectAsiLoaderIdentity(string gamePath)
    {
        if (!Directory.Exists(gamePath))
        {
            return ExistingModuleIdentity.None;
        }

        foreach (var fileName in AsiLoaderProbeFilenames)
        {
            var identity = IdentifyExistingModule(gamePath, fileName);
            if (identity.IsAsiLoader)
            {
                return identity;
            }
        }

        return ExistingModuleIdentity.None;
    }

    private static ExistingModuleIdentity DetectSpecialKIdentity(string gamePath)
    {
        if (!Directory.Exists(gamePath))
        {
            return ExistingModuleIdentity.None;
        }

        foreach (var fileName in SpecialKProbeFilenames)
        {
            var identity = IdentifyExistingModule(gamePath, fileName);
            if (identity.ProviderName.Equals("Special K", StringComparison.OrdinalIgnoreCase))
            {
                return identity;
            }
        }

        return ExistingModuleIdentity.None;
    }

    private static string GetRecommendedTargetFilename(string gamePath, string targetFilename, ExistingModuleIdentity existingModule, ExistingModuleIdentity asiLoader, ManagedChainRecommendation managedChain)
    {
        if (managedChain.IsValid)
        {
            return targetFilename;
        }

        if (asiLoader.IsAsiLoader &&
            !targetFilename.Equals("OptiScaler.asi", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(Path.Combine(gamePath, "OptiScaler.asi")))
        {
            return "OptiScaler.asi";
        }

        if (!existingModule.IsOptiScaler && !string.IsNullOrWhiteSpace(existingModule.ProviderName))
        {
            foreach (var candidate in PreferredAlternativeFilenames)
            {
                if (candidate.Equals(targetFilename, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(gamePath, candidate)))
                {
                    return candidate;
                }
            }
        }

        return string.Empty;
    }

    private static string BuildManagedChainInstructions(string providerName, string redirectedFilename)
    {
        if (providerName.Equals("ReShade", StringComparison.OrdinalIgnoreCase))
        {
            return $"OptiScaler can rename the current ReShade DLL to {redirectedFilename}, install itself as dxgi.dll, and set LoadReshade=true automatically.";
        }

        if (providerName.Equals("Special K", StringComparison.OrdinalIgnoreCase))
        {
            return $"OptiScaler can rename the current Special K DLL to {redirectedFilename}, create an empty {SpecialKDxgiMarkerName} file, and set LoadSpecialK=true automatically.";
        }

        return string.Empty;
    }

    private static string GetManagedChainDestinationFilename(string providerName, string sourcePath)
    {
        var is64Bit = TryIsPortableExecutable64Bit(sourcePath);
        if (is64Bit != true)
        {
            return string.Empty;
        }

        if (providerName.Equals("ReShade", StringComparison.OrdinalIgnoreCase))
        {
            return "ReShade64.dll";
        }

        if (providerName.Equals("Special K", StringComparison.OrdinalIgnoreCase))
        {
            return "SpecialK64.dll";
        }

        return string.Empty;
    }

    private static bool? TryIsPortableExecutable64Bit(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (reader.ReadUInt16() != 0x5A4D)
            {
                return null;
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return null;
            }

            var machine = reader.ReadUInt16();
            return machine switch
            {
                0x8664 or 0x0200 => true,
                0x014c => false,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadMetadataText(string path, out string details)
    {
        details = string.Empty;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            details = FirstNonEmpty(info.ProductName, info.FileDescription, info.OriginalFilename, info.InternalName, info.CompanyName);
            return string.Join(
                    "\n",
                    new[] { info.ProductName, info.FileDescription, info.OriginalFilename, info.InternalName, info.CompanyName }
                        .Where(value => !string.IsNullOrWhiteSpace(value)))
                .ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ExistingModuleIdentity(
        string SourceFilename,
        string ProviderName,
        string Details,
        bool IsOptiScaler,
        bool IsAsiLoader,
        bool CanBeChainedThroughOptiScaler,
        string AsiInstructions = "")
    {
        public static ExistingModuleIdentity None { get; } = new(string.Empty, string.Empty, string.Empty, false, false, false, string.Empty);
    }

    private sealed record ManagedChainRecommendation(
        string ProviderName,
        string SourceFilename,
        string RedirectedFilename,
        string Instructions,
        bool CreateSpecialKDxgiMarker)
    {
        public static ManagedChainRecommendation None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false);

        public bool IsValid => !string.IsNullOrWhiteSpace(ProviderName);
    }
}
