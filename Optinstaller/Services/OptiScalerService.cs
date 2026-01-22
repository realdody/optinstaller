using System;
using System.Collections.Generic;
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

    // Potential filenames user might choose
    private static readonly string[] PossibleFilenames = 
    { 
        "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", 
        "d3d12.dll", "wininet.dll", "winhttp.dll", "OptiScaler.asi" 
    };

    public bool IsInstalled(string gamePath, out string installedFilename)
    {
        installedFilename = string.Empty;
        
        // Check for OptiScaler.ini first as a strong indicator
        if (!File.Exists(Path.Combine(gamePath, OptiScalerIniName)))
            return false;

        // Check for any of the possible DLLs
        foreach (var file in PossibleFilenames)
        {
            if (File.Exists(Path.Combine(gamePath, file)))
            {
                installedFilename = file;
                return true;
            }
        }
        
        return false;
    }

    public async Task InstallAsync(InstallationOptions options)
    {
        await Task.Run(async () => 
        {
            var gamePath = options.GamePath;
            var versionPath = options.VersionPath;
            var targetFilename = options.TargetFilename;

            // Find the source DLL (OptiScaler.dll)
            var sourceDll = Path.Combine(versionPath, "OptiScaler.dll");
            
            if (!File.Exists(sourceDll))
                throw new FileNotFoundException($"OptiScaler.dll not found in {versionPath}.");

            var dest = Path.Combine(gamePath, targetFilename);

            // 1. Copy DLL
            // If target exists, delete it first (overwrite logic should be handled by caller prompt, but we force here)
            if (File.Exists(dest)) File.Delete(dest);
            File.Copy(sourceDll, dest, true);

            // 2. Copy/Create Config if missing
            var configPath = Path.Combine(gamePath, OptiScalerIniName);
            var sourceConfig = Path.Combine(versionPath, OptiScalerIniName);
            
            if (!File.Exists(configPath) && File.Exists(sourceConfig))
            {
                File.Copy(sourceConfig, configPath);
            }

            // 3. Configure Spoofing (Dxgi=auto/false)
            if (File.Exists(configPath))
            {
                var content = File.ReadAllText(configPath);
                
                // If not enabled (AMD/Intel), force false. If enabled (Nvidia), we generally leave as auto or default.
                // The bat script logic:
                // if (!enableSpoofing) -> replace 'Dxgi=auto' with 'Dxgi=false'
                if (!options.EnableSpoofing)
                {
                    content = content.Replace("Dxgi=auto", "Dxgi=false");
                }
                
                // If OptiPatcher is used, enable ASI loading
                if (options.UseOptiPatcher)
                {
                     content = content.Replace("LoadAsiPlugins=auto", "LoadAsiPlugins=true");
                     content = content.Replace("LoadAsiPlugins=false", "LoadAsiPlugins=true");
                }

                File.WriteAllText(configPath, content);
            }

            // 4. Download OptiPatcher if requested
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

            // 5. Create Uninstaller Bat (if requested)
            if (options.CreateUninstaller)
            {
                 CreateUninstallerBat(gamePath, targetFilename);
            }
        });
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
        sb.AppendLine("if \"%removeChoice%\"==\"y\" (");
        sb.AppendLine($"    del {OptiScalerLogName}");
        sb.AppendLine($"    del {OptiScalerIniName}");
        sb.AppendLine($"    del {filename}");
        sb.AppendLine("    del fakenvapi.dll");
        sb.AppendLine("    del fakenvapi.ini");
        sb.AppendLine("    del fakenvapi.log");
        sb.AppendLine("    del dlssg_to_fsr3_amd_is_better.dll");
        sb.AppendLine("    del dlssg_to_fsr3.log");
        sb.AppendLine("    if exist plugins\\OptiPatcher.asi del plugins\\OptiPatcher.asi");
        sb.AppendLine("    if exist plugins rmdir plugins");
        sb.AppendLine("    if exist D3D12_Optiscaler rmdir /s /q D3D12_Optiscaler");
        sb.AppendLine("    if exist DlssOverrides rmdir /s /q DlssOverrides");
        sb.AppendLine("    if exist Licenses rmdir /s /q Licenses");
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
            // 1. Get code from GitHub
            using var client = new HttpClient();
            var code = await client.GetStringAsync(OptiPatcherUrl);

            // 2. Scan local exes
            var exes = Directory.GetFiles(gamePath, "*.exe").Select(Path.GetFileName).ToList();
            if (!exes.Any()) return false;

            // 3. Regex Match logic
            // Match CHECK_UE(Name) -> Name-win64-shipping.exe
            var ueMatches = Regex.Matches(code, @"CHECK_UE\s*\(\s*([a-zA-Z0-9_]+)\s*\)");
            foreach (Match match in ueMatches)
            {
                if (match.Groups.Count > 1)
                {
                    var baseName = match.Groups[1].Value;
                    var win64 = $"{baseName}-win64-shipping.exe";
                    var wingdk = $"{baseName}-wingdk-shipping.exe";
                    
                    if (exes.Any(e => e.Equals(win64, StringComparison.OrdinalIgnoreCase) || 
                                      e.Equals(wingdk, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            // Match exact exeName == "Name.exe"
            // Simple approximation of the C++ logic: exeName == "Foo.exe"
            var directMatches = Regex.Matches(code, @"exeName\s*==\s*[\x22\x27]([^\x22\x27]+)[\x22\x27]");
            foreach (Match match in directMatches)
            {
                 if (match.Groups.Count > 1)
                 {
                     var name = match.Groups[1].Value;
                     if (exes.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase)))
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
            // List of files to remove based on the batch script
            var filesToRemove = new List<string>
            {
                OptiScalerLogName,
                OptiScalerIniName,
                installedFilename,
                "fakenvapi.dll",
                "fakenvapi.ini",
                "fakenvapi.log",
                "dlssg_to_fsr3_amd_is_better.dll",
                "dlssg_to_fsr3.log"
            };

            foreach (var file in filesToRemove)
            {
                var path = Path.Combine(gamePath, file);
                if (File.Exists(path)) File.Delete(path);
            }
            
            // Remove plugins/OptiPatcher.asi
            var patcher = Path.Combine(gamePath, "plugins", "OptiPatcher.asi");
            if (File.Exists(patcher)) File.Delete(patcher);
            
            var pluginsDir = Path.Combine(gamePath, "plugins");
            if (Directory.Exists(pluginsDir) && !Directory.EnumerateFileSystemEntries(pluginsDir).Any())
                Directory.Delete(pluginsDir);

            // Directories to remove
            var dirsToRemove = new[] { "D3D12_Optiscaler", "DlssOverrides", "Licenses" };
            foreach (var dir in dirsToRemove)
            {
                var path = Path.Combine(gamePath, dir);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        });
    }
}
