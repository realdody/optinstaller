using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Optinstaller.Messages;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class VersionManagerViewModel : ViewModelBase
{
    private readonly VersionService _versionService;

    // All versions from source
    private ObservableCollection<OptiScalerVersion> _allVersions = new();

    // Filtered versions for display
    [ObservableProperty]
    private ObservableCollection<OptiScalerVersion> _downloadedVersions = new();

    [ObservableProperty]
    private ObservableCollection<OptiScalerVersion> _availableVersions = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _downloadedCount;

    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private bool _hasDownloadedVersions;

    [ObservableProperty]
    private bool _hasAvailableVersions;

    public VersionManagerViewModel()
    {
        _versionService = new VersionService();
    }

    public async Task InitializeAsync()
    {
        await LoadVersions();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(query)
            ? _allVersions.ToList()
            : _allVersions.Where(v =>
                (v.TagName ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Name ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (v.Description ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
            ).ToList();

        DownloadedVersions.Clear();
        AvailableVersions.Clear();

        foreach (var v in filtered.Where(x => x.IsDownloaded).OrderByDescending(x => x.PublishedAt))
        {
            DownloadedVersions.Add(v);
        }

        foreach (var v in filtered.Where(x => !x.IsDownloaded).OrderByDescending(x => x.PublishedAt))
        {
            AvailableVersions.Add(v);
        }

        FilteredCount = filtered.Count;
        HasDownloadedVersions = DownloadedVersions.Count > 0;
        HasAvailableVersions = AvailableVersions.Count > 0;
    }

    [RelayCommand]
    private async Task LoadVersions()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        _allVersions.Clear();

        try
        {
            var versions = await _versionService.GetAvailableVersionsAsync();
            foreach (var v in versions)
            {
                _allVersions.Add(v);
            }

            TotalCount = _allVersions.Count;
            DownloadedCount = _allVersions.Count(v => v.IsDownloaded);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load versions: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private async Task DownloadVersion(OptiScalerVersion version)
    {
        if (version.IsDownloading) return;

        version.IsDownloading = true;
        version.DownloadProgress = 0;
        version.DownloadStatus = "Starting download...";
        ErrorMessage = string.Empty;

        var progress = new Progress<double>(p =>
        {
            version.DownloadProgress = p;
            version.DownloadStatus = p < 100 
                ? $"Downloading... {p:F0}%" 
                : "Extracting...";
        });

        var versionTagName = version.TagName;
        try
        {
            await _versionService.DownloadVersionAsync(version, progress);
            version.DownloadStatus = "Completed!";
            
            // Refresh the list to update status
            await LoadVersions();
            WeakReferenceMessenger.Default.Send(new VersionsChangedMessage(true));
        }
        catch (Exception ex)
        {
            version.DownloadStatus = $"Failed: {ex.Message}";
            ErrorMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            // Find the matching refreshed item in _allVersions by its unique TagName
            // and update its properties so the UI reflects the final state
            var refreshedVersion = _allVersions.FirstOrDefault(v => v.TagName == versionTagName);
            if (refreshedVersion != null)
            {
                refreshedVersion.IsDownloading = false;
                refreshedVersion.DownloadProgress = 0;
                if (!string.IsNullOrEmpty(version.DownloadStatus) && !version.DownloadStatus.StartsWith("Failed"))
                {
                    refreshedVersion.DownloadStatus = version.DownloadStatus;
                }
            }
            else
            {
                // Fallback: update the original object if not found in refreshed list
                version.IsDownloading = false;
                version.DownloadProgress = 0;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteVersion(OptiScalerVersion version)
    {
        try
        {
            _versionService.DeleteVersion(version);
            await LoadVersions();
            WeakReferenceMessenger.Default.Send(new VersionsChangedMessage(true));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder(OptiScalerVersion version)
    {
        if (string.IsNullOrEmpty(version.LocalPath) || !System.IO.Directory.Exists(version.LocalPath))
        {
            ErrorMessage = "Folder not found for this version.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = version.LocalPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to open folder: {ex.Message}";
        }
    }
}
