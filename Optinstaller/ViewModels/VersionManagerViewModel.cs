using System;
using System.Collections.ObjectModel;
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
    }

    public async Task InitializeAsync()
    {
        await LoadVersions();
    }

    [RelayCommand]
    private async Task LoadVersions()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        Versions.Clear();
        
        try 
        {
            var versions = await _versionService.GetAvailableVersionsAsync();
            foreach (var v in versions)
            {
                Versions.Add(v);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading versions: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
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
            await LoadVersions();
            WeakReferenceMessenger.Default.Send(new VersionsChangedMessage(true));
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
}
