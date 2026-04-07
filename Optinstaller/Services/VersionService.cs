using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Optinstaller.Models;
using SharpSevenZip;

namespace Optinstaller.Services;

public class VersionService
{
    private static readonly Regex VersionTagPattern = new(@"\bv?(\d+(?:\.\d+)+(?:-[A-Za-z0-9.-]+)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
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

        _httpClient = SharedHttpClient;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Optinstaller/1.0 (OptiScaler Manager)");
        return httpClient;
    }

    public async Task<List<OptiScalerVersion>> GetAvailableVersionsAsync()
    {
        var versions = new List<OptiScalerVersion>();

        // Fetch from primary source (Official)
        try
        {
            foreach (var url in _gitHubApiUrls)
            {
                try
                {
                    var releases = await _httpClient.GetFromJsonAsync(url, VersionServiceJsonContext.Default.ListGitHubRelease);
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

        // Scan local Versions directory for folders that might not be in the GitHub list (or if offline)
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
                    // Determine source from directory name or default to Official
                    var source = dirName.Contains("bleeding", StringComparison.OrdinalIgnoreCase) || 
                                 dirName.Contains("edge", StringComparison.OrdinalIgnoreCase)
                        ? "BleedingEdge" 
                        : "Official";
                    
                    versions.Add(new OptiScalerVersion
                    {
                        Name = dirName,
                        TagName = dirName,
                        Description = "Locally installed version",
                        PublishedAt = Directory.GetCreationTime(dir),
                        IsDownloaded = true,
                        LocalPath = dir,
                        Source = source
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

    public List<OptiScalerVersion> GetDownloadedVersions()
    {
        var versions = new List<OptiScalerVersion>();
        if (!Directory.Exists(_versionsDirectory))
        {
            return versions;
        }

        foreach (var dir in Directory.GetDirectories(_versionsDirectory))
        {
            var dirName = Path.GetFileName(dir);
            if (!File.Exists(Path.Combine(dir, "OptiScaler.dll")))
            {
                continue;
            }

            var source = dirName.Contains("bleeding", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Contains("edge", StringComparison.OrdinalIgnoreCase)
                ? "BleedingEdge"
                : "Official";

            versions.Add(new OptiScalerVersion
            {
                Name = dirName,
                TagName = dirName,
                Description = "Locally installed version",
                PublishedAt = Directory.GetCreationTimeUtc(dir),
                IsDownloaded = true,
                LocalPath = dir,
                Source = source,
            });
        }

        return versions
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

            PrepareExtractedVersionDirectory(tempDestDir);

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

    public async Task<string> ImportVersionArchiveAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Choose a .zip or .7z archive to import.", nameof(archivePath));
        }

        var fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException("The selected archive could not be found.", fullArchivePath);
        }

        var extension = Path.GetExtension(fullArchivePath);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .zip and .7z OptiScaler archives can be imported.");
        }

        var tempDestDir = Path.Combine(_versionsDirectory, $".import_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDestDir);
            await ExtractArchiveAsync(fullArchivePath, Path.GetFullPath(tempDestDir));
            PrepareExtractedVersionDirectory(tempDestDir);

            var importedTagName = ResolveImportedVersionTagName(fullArchivePath, tempDestDir);
            var destinationDirectory = Path.Combine(_versionsDirectory, importedTagName);
            if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, true);
            }

            Directory.Move(tempDestDir, destinationDirectory);
            return importedTagName;
        }
        catch
        {
            if (Directory.Exists(tempDestDir))
            {
                try { Directory.Delete(tempDestDir, true); } catch { /* ignore cleanup errors */ }
            }

            throw;
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
        var assemblyPath = AppDomain.CurrentDomain.BaseDirectory;

        var candidatePaths = GetSevenZipLibraryCandidatePaths(assemblyPath);
        var libPath = candidatePaths.FirstOrDefault(File.Exists);
        if (libPath == null)
        {
            throw new FileNotFoundException($"Could not locate the SharpSevenZip native library. Searched: {string.Join(", ", candidatePaths)}");
        }

        SharpSevenZipBase.SetLibraryPath(libPath);
    }

    private static string[] GetSevenZipLibraryCandidatePaths(string assemblyPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return
            [
                Path.Combine(assemblyPath, "x64", "7z.dll"),
                Path.Combine(assemblyPath, "7z64.dll"),
                Path.Combine(assemblyPath, "7z.dll")
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return [Path.Combine(assemblyPath, "lib7z.so")];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return [Path.Combine(assemblyPath, "lib7z.dylib")];
        }

        return
        [
            Path.Combine(assemblyPath, "7z64.dll"),
            Path.Combine(assemblyPath, "7z.dll")
        ];
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

    private static void PrepareExtractedVersionDirectory(string destDir)
    {
        FlattenNestedFolders(destDir);
        PromoteNestedOptiScalerDirectory(destDir);
        ValidateExtractedVersionDirectory(destDir);
    }

    private static void ValidateExtractedVersionDirectory(string destDir)
    {
        if (!File.Exists(Path.Combine(destDir, "OptiScaler.dll")))
        {
            throw new InvalidOperationException("Archive does not contain OptiScaler.dll at the expected root level.");
        }
    }

    private static void PromoteNestedOptiScalerDirectory(string destDir)
    {
        if (File.Exists(Path.Combine(destDir, "OptiScaler.dll")))
        {
            return;
        }

        string[] candidateDllPaths;
        try
        {
            candidateDllPaths = Directory.GetFiles(destDir, "OptiScaler.dll", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        if (candidateDllPaths.Length != 1)
        {
            return;
        }

        var nestedDirectory = Path.GetDirectoryName(candidateDllPaths[0]);
        if (string.IsNullOrWhiteSpace(nestedDirectory) ||
            Path.GetFullPath(nestedDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(nestedDirectory))
        {
            var destinationFile = Path.Combine(destDir, Path.GetFileName(file));
            if (File.Exists(destinationFile))
            {
                File.Delete(destinationFile);
            }

            File.Move(file, destinationFile);
        }

        foreach (var directory in Directory.GetDirectories(nestedDirectory))
        {
            var destinationSubDirectory = Path.Combine(destDir, Path.GetFileName(directory));
            if (Directory.Exists(destinationSubDirectory))
            {
                MergeDirectoriesStatic(directory, destinationSubDirectory);
                Directory.Delete(directory, true);
            }
            else
            {
                Directory.Move(directory, destinationSubDirectory);
            }
        }

        TryDeleteEmptyDirectoryChain(nestedDirectory, destDir);
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

    private static void TryDeleteEmptyDirectoryChain(string directoryPath, string stopDirectory)
    {
        var stopFullPath = Path.GetFullPath(stopDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentDirectory = new DirectoryInfo(directoryPath);

        while (currentDirectory.Exists)
        {
            var currentFullPath = currentDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (currentFullPath.Equals(stopFullPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (currentDirectory.EnumerateFileSystemInfos().Any())
            {
                break;
            }

            var parentDirectory = currentDirectory.Parent;
            currentDirectory.Delete();
            if (parentDirectory == null)
            {
                break;
            }

            currentDirectory = parentDirectory;
        }
    }

    private static string ResolveImportedVersionTagName(string archivePath, string extractedDirectory)
    {
        var archiveFileName = Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty;
        var hasBleedingEdgeHint = archiveFileName.Contains("bleeding", StringComparison.OrdinalIgnoreCase) ||
                                  archiveFileName.Contains("edge", StringComparison.OrdinalIgnoreCase);

        var versionFromArchiveName = ExtractVersionTag(archiveFileName, hasBleedingEdgeHint);
        if (!string.IsNullOrWhiteSpace(versionFromArchiveName))
        {
            return versionFromArchiveName;
        }

        var dllPath = Path.Combine(extractedDirectory, "OptiScaler.dll");
        var versionFromDll = TryReadVersionTagFromDll(dllPath, hasBleedingEdgeHint);
        if (!string.IsNullOrWhiteSpace(versionFromDll))
        {
            return versionFromDll;
        }

        var sanitizedArchiveName = SanitizeVersionDirectoryName(archiveFileName);
        if (!string.IsNullOrWhiteSpace(sanitizedArchiveName))
        {
            return sanitizedArchiveName;
        }

        return $"imported-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
    }

    private static string? TryReadVersionTagFromDll(string dllPath, bool hasBleedingEdgeHint)
    {
        if (!File.Exists(dllPath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return ExtractVersionTag(info.ProductVersion, hasBleedingEdgeHint) ??
                   ExtractVersionTag(info.FileVersion, hasBleedingEdgeHint);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractVersionTag(string? value, bool hasBleedingEdgeHint)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = VersionTagPattern.Match(value);
        if (!match.Success)
        {
            return null;
        }

        var versionValue = $"v{match.Groups[1].Value}";
        return hasBleedingEdgeHint ? $"bleeding-edge-{versionValue}" : versionValue;
    }

    private static string SanitizeVersionDirectoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidCharacter, '-');
        }

        sanitized = Regex.Replace(sanitized, @"\s+", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-").Trim('-');
        return sanitized;
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

}

internal sealed class GitHubRelease
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

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
