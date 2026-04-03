using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
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

    private static readonly string[] PossibleFilenames = 
    { 
        "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", 
        "d3d12.dll", "wininet.dll", "winhttp.dll", "OptiScaler.asi" 
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

                var patcherDest = Path.Combine(pluginsDir, "OptiPatcher.asi");
                
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
        sb.AppendLine("    if exist \"plugins\\OptiPatcher.asi\" del \"plugins\\OptiPatcher.asi\"");
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
        });
    }
}
