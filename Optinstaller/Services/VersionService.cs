using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Optinstaller.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Optinstaller.Services;

public class VersionService
{
    private readonly string[] _gitHubApiUrls = 
    {
        "https://api.github.com/repos/OptiScaler/OptiScaler/releases",
        "https://api.github.com/repos/realdody/OptiScaler-Bleeding-Edge/releases"
    };
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
            foreach (var url in _gitHubApiUrls)
            {
                try
                {
                    var releases = await _httpClient.GetFromJsonAsync<List<GitHubRelease>>(url);
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
                    Console.WriteLine($"Error fetching releases from {url}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in version fetching process: {ex.Message}");
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

    public async Task DownloadVersionAsync(OptiScalerVersion version, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(version.DownloadUrl)) return;

        var fileName = Path.GetFileName(new Uri(version.DownloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName)) 
        {
             fileName = version.DownloadUrl.EndsWith(".7z") ? $"{version.TagName}.7z" : $"{version.TagName}.zip";
        }
        
        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        
        try
        {
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
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    var read = 0;

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

            var destDir = Path.Combine(_versionsDirectory, version.TagName);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            Directory.CreateDirectory(destDir);
            
            var destDirFullPath = Path.GetFullPath(destDir);

            if (tempFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                 using (var archive = ZipFile.OpenRead(tempFile))
                 {
                     foreach (var entry in archive.Entries)
                     {
                         if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/")) 
                         {
                             continue;
                         }

                         var destPath = Path.GetFullPath(Path.Combine(destDirFullPath, entry.FullName));
                         if (!destPath.StartsWith(destDirFullPath + Path.DirectorySeparatorChar) &&
                             !destPath.StartsWith(destDirFullPath + Path.AltDirectorySeparatorChar))
                         {
                             throw new IOException("Zip slip attempt detected.");
                         }
                         
                         var entryDir = Path.GetDirectoryName(destPath);
                         if (!Directory.Exists(entryDir)) Directory.CreateDirectory(entryDir!);
                         
                         if (!string.IsNullOrEmpty(entry.Name))
                         {
                            entry.ExtractToFile(destPath, true);
                         }
                     }
                 }
            }
            else
            {
                 using (var archive = ArchiveFactory.Open(tempFile))
                 {
                     foreach (var entry in archive.Entries)
                     {
                         if (!entry.IsDirectory)
                         {
                             var entryPath = entry.Key;
                             if (entryPath == null) continue;

                             var destPath = Path.GetFullPath(Path.Combine(destDirFullPath, entryPath));

                             if (!destPath.StartsWith(destDirFullPath + Path.DirectorySeparatorChar) &&
                                 !destPath.StartsWith(destDirFullPath + Path.AltDirectorySeparatorChar))
                             {
                                 throw new IOException("Zip slip attempt detected.");
                             }

                             entry.WriteToDirectory(destDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                         }
                     }
                 }
            }
            
            // Check for nested folders (sometimes zips have a root folder)
            var subDirs = Directory.GetDirectories(destDir);
            var files = Directory.GetFiles(destDir);
            
            while (files.Length == 0 && subDirs.Length == 1)
            {
                var nestedDir = subDirs[0];
                
                foreach (var file in Directory.GetFiles(nestedDir))
                {
                    var destFile = Path.Combine(destDir, Path.GetFileName(file));
                    if (File.Exists(destFile)) File.Delete(destFile);
                    File.Move(file, destFile);
                }
                
                foreach (var dir in Directory.GetDirectories(nestedDir))
                {
                    var dirName = Path.GetFileName(dir);
                    var destSubDir = Path.Combine(destDir, dirName);
                    
                    if (Directory.Exists(destSubDir))
                    {
                        MergeDirectories(dir, destSubDir);
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

            version.IsDownloaded = true;
            version.LocalPath = destDir;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private void MergeDirectories(string sourceDir, string destDir)
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
            MergeDirectories(dir, destSubDir);
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
