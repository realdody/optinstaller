using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class VersionManagerViewModel : ViewModelBase
{
    private readonly VersionService _versionService;

    [ObservableProperty]
    private ObservableCollection<OptiScalerVersion> _versions = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public VersionManagerViewModel()
    {
        _versionService = new VersionService();
        _ = LoadVersions();
    }

    [RelayCommand]
    private async Task LoadVersions()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        Versions.Clear();
        
        var versions = await _versionService.GetAvailableVersionsAsync();
        foreach (var v in versions)
        {
            Versions.Add(v);
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task DownloadVersion(OptiScalerVersion version)
    {
        if (IsDownloading) return;

        IsDownloading = true;
        ErrorMessage = string.Empty;
        DownloadProgress = 0;

        var progress = new Progress<double>(p => DownloadProgress = p);

        try
        {
            await _versionService.DownloadVersionAsync(version, progress);
            // Refresh state
            var index = Versions.IndexOf(version);
            if (index != -1)
            {
                // Force UI update if needed, though property change should handle it if object is same reference
                Versions[index] = version; 
                OnPropertyChanged(nameof(Versions));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
        }
    }

    [RelayCommand]
    private void DeleteVersion(OptiScalerVersion version)
    {
        try
        {
            _versionService.DeleteVersion(version);
            // Refresh UI state
            var index = Versions.IndexOf(version);
            if (index != -1)
            {
                Versions[index] = version; // Trigger update
                OnPropertyChanged(nameof(Versions));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }
}
