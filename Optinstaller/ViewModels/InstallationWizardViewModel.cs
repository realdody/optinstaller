using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class InstallationWizardViewModel : ViewModelBase
{
    private readonly OptiScalerService _optiScalerService;
    private readonly InstallationOptions _options;

    [ObservableProperty] private int _stepIndex = 0;
    [ObservableProperty] private string _title = "Welcome";
    [ObservableProperty] private bool _canGoNext = true;
    [ObservableProperty] private bool _canGoBack = false;
    [ObservableProperty] private string _nextButtonText = "Next";

    [ObservableProperty] private bool _showEngineWarning;
    [ObservableProperty] private bool _isCheckingEnvironment;
    
    [ObservableProperty] private ObservableCollection<OptiScalerVersion> _availableVersions;
    [ObservableProperty] private OptiScalerVersion? _selectedVersion;

    [ObservableProperty] private string _selectedFilename = "dxgi.dll";
    [ObservableProperty] private bool _fileExistsWarning;
    
    [ObservableProperty] private bool _isNvidia;
    [ObservableProperty] private bool _enableSpoofing = true;
    [ObservableProperty] private bool _isWine;
    [ObservableProperty] private string _gpuName = "Detecting...";
    
    [ObservableProperty] private bool _checkingOptiPatcher;
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(OptiPatcherSeverity))]
    private bool _optiPatcherSupported;
    [ObservableProperty] private bool _useOptiPatcher;
    [ObservableProperty] private string _optiPatcherStatus = "Checking compatibility...";
    
    public InfoBarSeverity OptiPatcherSeverity => OptiPatcherSupported ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    
    [ObservableProperty] private bool _createUninstaller = true;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _installStatus = "";
    [ObservableProperty] private bool _installSuccess;

    public InstallationOptions Options => _options;

    public List<string> Filenames => new()
    {
        "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll",
        "d3d12.dll", "wininet.dll", "winhttp.dll", "OptiScaler.asi"
    };

    public bool IsStep0 => StepIndex == 0;
    public bool IsStep1 => StepIndex == 1;
    public bool IsStep2 => StepIndex == 2;
    public bool IsStep3 => StepIndex == 3;
    public bool IsStep4 => StepIndex == 4;
    public bool IsStep5 => StepIndex == 5;
    public bool IsStep6 => StepIndex == 6;

    public event EventHandler? RequestClose;

    public InstallationWizardViewModel(GameInstance game, IEnumerable<OptiScalerVersion> availableVersions, OptiScalerVersion? defaultVersion = null)
    {
        if (availableVersions == null) throw new ArgumentNullException(nameof(availableVersions));

        _optiScalerService = new OptiScalerService();
        _availableVersions = new ObservableCollection<OptiScalerVersion>(availableVersions);
        _selectedVersion = defaultVersion ?? _availableVersions.FirstOrDefault();
        
        _options = new InstallationOptions
        {
            GamePath = game.GamePath,
            VersionPath = _selectedVersion?.LocalPath ?? string.Empty
        };

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            IsCheckingEnvironment = true;

            if (Directory.Exists(Path.Combine(_options.GamePath, "Engine")))
            {
                ShowEngineWarning = true;
            }

            // In .NET cross-platform, difficult to check registry easily without platform guards.
            // We'll assume Windows logic primarily as requested.
            _options.IsWine = CheckWine();
            IsWine = _options.IsWine;

            await CheckGpu();

            IsCheckingEnvironment = false;
            UpdateState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initialization failed: {ex}");
            IsCheckingEnvironment = false;
        }
    }

    private bool CheckWine()
    {
        // Simple heuristic: Z: drive mapping usually exists in Wine
        return Environment.GetEnvironmentVariable("WINEDLLPATH") != null;
    }

    private async Task CheckGpu()
    {
        if (IsWine)
        {
            GpuName = "Wine Environment (Skipping Detection)";
            IsNvidia = false; 
            EnableSpoofing = true;
            return;
        }

        bool foundNvidia = false;
        
        await Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (var obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        {
                            foundNvidia = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to nvapi check
                var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (File.Exists(Path.Combine(system32, "nvapi64.dll"))) foundNvidia = true;
            }
        });

        if (foundNvidia)
        {
            IsNvidia = true;
            GpuName = "Nvidia GPU Detected";
            EnableSpoofing = true;
        }
        else
        {
            IsNvidia = false;
            GpuName = "AMD/Intel GPU Detected";
            EnableSpoofing = true;
        }
    }

    [RelayCommand]
    private async Task Next()
    {
        if (IsInstalling) return;

        if (StepIndex == 0 && ShowEngineWarning)
        {
        }

        if (StepIndex == 1)
        {
            if (SelectedVersion == null) return;
            _options.VersionPath = SelectedVersion.LocalPath;
        }

        if (StepIndex == 2)
        {
            var path = Path.Combine(_options.GamePath, SelectedFilename);
            if (File.Exists(path) && !FileExistsWarning)
            {
                FileExistsWarning = true;
                return;
            }
            _options.TargetFilename = SelectedFilename;
        }

        if (StepIndex == 3)
        {
            _options.EnableSpoofing = EnableSpoofing;
            
            if (IsNvidia)
            {
                 // Skip OptiPatcher on Nvidia
                 StepIndex++;
                 StepIndex++;
                 UpdateState();
                 return;
            }

            CheckingOptiPatcher = true;
            OptiPatcherStatus = "Checking GitHub for compatibility...";
            var supported = await _optiScalerService.CheckOptiPatcherSupportAsync(_options.GamePath);
            OptiPatcherSupported = supported;
            CheckingOptiPatcher = false;
            if (supported)
            {
                OptiPatcherStatus = "OptiPatcher support detected! Highly recommended for this game.";
                UseOptiPatcher = true;
            }
            else
            {
                OptiPatcherStatus = "No known OptiPatcher support detected for this game.";
                UseOptiPatcher = false;
            }
        }
        
        if (StepIndex == 5)
        {
             IsInstalling = true;
             UpdateState();
             try
             {
                await Install();
             }
             finally
             {
                IsInstalling = false;
                UpdateState();
             }
             return;
        }

        StepIndex++;
        UpdateState();
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex > 0)
        {
            StepIndex--;
            if (StepIndex == 4 && IsNvidia)
            {
                StepIndex--;
            }
            UpdateState();
        }
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ForceOptiPatcher()
    {
        OptiPatcherSupported = true;
        UseOptiPatcher = true;
        OptiPatcherStatus = "Force installed enabled by user.";
    }

    private void UpdateState()
    {
        CanGoBack = StepIndex > 0 && !IsInstalling && !InstallSuccess;
        
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
        OnPropertyChanged(nameof(IsStep6));
        
        switch (StepIndex)
        {
            case 0: 
                Title = "Welcome"; 
                NextButtonText = "Next";
                break;
            case 1:
                Title = "Select Version";
                break;
            case 2: 
                Title = "Select Filename"; 
                FileExistsWarning = false;
                break;
            case 3: 
                Title = "Configuration"; 
                break;
            case 4: 
                Title = "OptiPatcher"; 
                break;
            case 5: 
                Title = "Ready to Install"; 
                NextButtonText = "Install";
                break;
            case 6:
                Title = "Finished";
                CanGoBack = false;
                CanGoNext = false;
                break;
        }
    }

    private async Task Install()
    {
        InstallStatus = "Installing OptiScaler...";
        _options.UseOptiPatcher = UseOptiPatcher;
        _options.CreateUninstaller = CreateUninstaller;

        try
        {
            await _optiScalerService.InstallAsync(_options);
            InstallSuccess = true;
            InstallStatus = "Installation Complete!";
            StepIndex++;
        }
        catch (Exception ex)
        {
            InstallStatus = $"Error: {ex.Message}";
        }
    }
}
