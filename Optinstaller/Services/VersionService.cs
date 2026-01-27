using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Optinstaller.Models;
using SharpSevenZip;

namespace Optinstaller.Services;

public class VersionService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/OptiScaler/OptiScaler/releases";
    private readonly string _versionsDirectory;
    private readonly HttpClient _httpClient;

    public VersionService()
    {
        _versionsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Versions");
        if (!Directory.Exists(_versionsDirectory))
        {
            Directory.CreateDirectory(_versionsDirectory);
        }

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Optinstaller/1.0 (OptiScaler Manager)");
    }

    public async Task<List<OptiScalerVersion>> GetAvailableVersionsAsync()
    {
        var versions = new List<OptiScalerVersion>();

        try
        {
            var releases = await _httpClient.GetFromJsonAsync<List<GitHubRelease>>(GitHubApiUrl);
            if (releases != null)
            {
                foreach (var release in releases)
                {
                    var asset = release.Assets?.FirstOrDefault(a => 
                        a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || 
                        a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
                    
                    if (asset == null) continue;

                    var version = new OptiScalerVersion
                    {
                        Name = release.Name ?? release.TagName,
                        TagName = release.TagName,
                        Description = release.Description,
                        PublishedAt = release.PublishedAt,
                        DownloadUrl = asset.BrowserDownloadUrl,
                        FileSize = asset.Size
                    };

                    CheckLocalStatus(version);
                    versions.Add(version);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching releases: {ex.Message}");
        }

        // 2. Scan local Versions directory for folders that might not be in the GitHub list (or if offline)
        // This ensures installed versions show up even if GitHub is down
        if (Directory.Exists(_versionsDirectory))
        {
            var directories = Directory.GetDirectories(_versionsDirectory);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (versions.Any(v => v.TagName.Equals(dirName, StringComparison.OrdinalIgnoreCase))) continue;

                if (File.Exists(Path.Combine(dir, "OptiScaler.dll")))
                {
                    versions.Add(new OptiScalerVersion
                    {
                        Name = dirName,
                        TagName = dirName,
                        Description = "Locally installed version",
                        PublishedAt = Directory.GetCreationTime(dir),
                        IsDownloaded = true,
                        LocalPath = dir
                    });
                }
            }
        }

        return versions
            .GroupBy(v => v.TagName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.IsDownloaded || !string.IsNullOrEmpty(v.LocalPath)).ThenByDescending(v => v.PublishedAt).First())
            .OrderByDescending(v => v.PublishedAt)
            .ToList();
    }

    private void CheckLocalStatus(OptiScalerVersion version)
    {
        var folderName = version.TagName;
        var versionPath = Path.Combine(_versionsDirectory, folderName);
        var dllPath = Path.Combine(versionPath, "OptiScaler.dll");

        if (Directory.Exists(versionPath) && File.Exists(dllPath))
        {
            version.IsDownloaded = true;
            version.LocalPath = versionPath;
        }
        else
        {
            version.IsDownloaded = false;
        }
    }

    private const int BufferSize = 81920;

    public async Task DownloadVersionAsync(OptiScalerVersion version, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(version.DownloadUrl)) return;

        var fileName = Path.GetFileName(new Uri(version.DownloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName)) 
        {
            fileName = version.DownloadUrl.EndsWith(".7z") ? $"{version.TagName}.7z" : $"{version.TagName}.zip";
        }
        
        var tempFile = Path.Combine(Path.GetTempPath(), $"optinstaller_{Guid.NewGuid()}_{fileName}");
        var destDir = Path.Combine(_versionsDirectory, version.TagName);
        var tempDestDir = destDir + ".tmp";
        
        try
        {
            // Download the file
            using (var response = await _httpClient.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                
                // Verify content type isn't text (like an error page)
                if (response.Content.Headers.ContentType?.MediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new InvalidOperationException("Download URL returned text instead of a binary file. Possible invalid URL or API rate limit.");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progress != null;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
                {
                    var buffer = new byte[BufferSize];
                    var totalRead = 0L;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (canReportProgress)
                        {
                            progress?.Report((double)totalRead / totalBytes * 100);
                        }
                    }
                }
            }

            // Clean up any previous temp directory
            if (Directory.Exists(tempDestDir))
            {
                Directory.Delete(tempDestDir, true);
            }
            Directory.CreateDirectory(tempDestDir);
            
            var tempDestDirFullPath = Path.GetFullPath(tempDestDir);

            // Extract archive using SharpSevenZip (handles both .zip and .7z)
            await ExtractArchiveAsync(tempFile, tempDestDirFullPath);
            
            // Flatten nested folders (sometimes archives have a single root folder)
            FlattenNestedFolders(tempDestDir);

            // Validate that we have the expected OptiScaler.dll
            if (!File.Exists(Path.Combine(tempDestDir, "OptiScaler.dll")))
            {
                throw new InvalidOperationException("Archive does not contain OptiScaler.dll at root level.");
            }

            // Move to final destination (atomic-ish operation)
            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, true);
            }
            Directory.Move(tempDestDir, destDir);

            version.IsDownloaded = true;
            version.LocalPath = destDir;
        }
        catch
        {
            // Clean up partial extraction on failure
            if (Directory.Exists(tempDestDir))
            {
                try { Directory.Delete(tempDestDir, true); } catch { /* ignore cleanup errors */ }
            }
            throw;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destDirFullPath)
    {
        // Initialize SharpSevenZip with the appropriate 7z library
        SetSevenZipLibraryPath();

        await Task.Run(() =>
        {
            using var extractor = new SharpSevenZipExtractor(archivePath);
            
            // Validate each entry before extraction to prevent zip slip
            foreach (var entry in extractor.ArchiveFileData)
            {
                if (entry.IsDirectory) continue;
                
                var entryPath = entry.FileName;
                if (string.IsNullOrEmpty(entryPath)) continue;
                
                // Normalize path separators
                entryPath = entryPath.Replace('/', Path.DirectorySeparatorChar)
                                     .Replace('\\', Path.DirectorySeparatorChar);
                
                var destPath = Path.GetFullPath(Path.Combine(destDirFullPath, entryPath));
                
                // Zip slip protection with case-insensitive comparison (important for Windows)
                if (!IsPathWithinDirectory(destPath, destDirFullPath))
                {
                    throw new IOException($"Zip slip attempt detected for entry: {entry.FileName}");
                }
            }
            
            // Extract all files
            extractor.ExtractArchive(destDirFullPath);
        });
    }

    private static void SetSevenZipLibraryPath()
    {
        // SharpSevenZip needs the path to 7z.dll
        // It's bundled with the package, but we need to set the path based on architecture
        var assemblyPath = Path.GetDirectoryName(typeof(SharpSevenZipExtractor).Assembly.Location) 
                          ?? AppDomain.CurrentDomain.BaseDirectory;
        
        string libName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            libName = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "7z64.dll",
                Architecture.X86 => "7z.dll",
                Architecture.Arm64 => "7z64.dll",
                _ => "7z64.dll"
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            libName = "lib7z.so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            libName = "lib7z.dylib";
        }
        else
        {
            libName = "7z64.dll"; // fallback
        }

        var libPath = Path.Combine(assemblyPath, libName);
        if (File.Exists(libPath))
        {
            SharpSevenZipBase.SetLibraryPath(libPath);
        }
        // If lib doesn't exist at expected path, SharpSevenZip will try to find it automatically
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        // Use case-insensitive comparison on Windows
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? StringComparison.OrdinalIgnoreCase 
            : StringComparison.Ordinal;
        
        return fullPath.StartsWith(fullDir + Path.DirectorySeparatorChar, comparison) ||
               fullPath.Equals(fullDir, comparison);
    }

    private static void FlattenNestedFolders(string destDir)
    {
        var subDirs = Directory.GetDirectories(destDir);
        var files = Directory.GetFiles(destDir);
        
        // Keep flattening while there's only one subfolder and no files at root
        while (files.Length == 0 && subDirs.Length == 1)
        {
            var nestedDir = subDirs[0];
            
            // Move files from nested directory to parent
            foreach (var file in Directory.GetFiles(nestedDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                if (File.Exists(destFile)) File.Delete(destFile);
                File.Move(file, destFile);
            }
            
            // Move subdirectories from nested directory to parent
            foreach (var dir in Directory.GetDirectories(nestedDir))
            {
                var dirName = Path.GetFileName(dir);
                var destSubDir = Path.Combine(destDir, dirName);
                
                if (Directory.Exists(destSubDir))
                {
                    MergeDirectoriesStatic(dir, destSubDir);
                    Directory.Delete(dir, true);
                }
                else
                {
                    Directory.Move(dir, destSubDir);
                }
            }
            
            Directory.Delete(nestedDir);
            
            subDirs = Directory.GetDirectories(destDir);
            files = Directory.GetFiles(destDir);
        }
    }

    private static void MergeDirectoriesStatic(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (File.Exists(destFile)) File.Delete(destFile);
            File.Move(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            MergeDirectoriesStatic(dir, destSubDir);
        }
    }

    public void DeleteVersion(OptiScalerVersion version)
    {
        var dir = Path.Combine(_versionsDirectory, version.TagName);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
        version.IsDownloaded = false;
        version.LocalPath = string.Empty;
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string Description { get; set; } = "";

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
