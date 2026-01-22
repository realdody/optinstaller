using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // Step 1: Warnings
    [ObservableProperty] private bool _showEngineWarning;
    [ObservableProperty] private bool _isCheckingEnvironment;
    
    // Step 2: Filename
    [ObservableProperty] private string _selectedFilename = "dxgi.dll";
    [ObservableProperty] private bool _fileExistsWarning;
    
    // Step 3: GPU/Spoofing
    [ObservableProperty] private bool _isNvidia;
    [ObservableProperty] private bool _enableSpoofing = true;
    [ObservableProperty] private bool _isWine;
    [ObservableProperty] private string _gpuName = "Detecting...";
    
    // Step 4: OptiPatcher
    [ObservableProperty] private bool _checkingOptiPatcher;
    [ObservableProperty] private bool _optiPatcherSupported;
    [ObservableProperty] private bool _useOptiPatcher;
    [ObservableProperty] private string _optiPatcherStatus = "Checking compatibility...";
    
    // Step 5: Summary
    [ObservableProperty] private bool _createUninstaller = true;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _installStatus = "";
    [ObservableProperty] private bool _installSuccess;

    public List<string> Filenames => new()
    {
        "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll",
        "d3d12.dll", "wininet.dll", "winhttp.dll", "OptiScaler.asi"
    };

    // Wizard Page Visibility Helpers
    public bool IsStep0 => StepIndex == 0;
    public bool IsStep1 => StepIndex == 1;
    public bool IsStep2 => StepIndex == 2;
    public bool IsStep3 => StepIndex == 3;
    public bool IsStep4 => StepIndex == 4;
    public bool IsStep5 => StepIndex == 5;

    public event EventHandler? RequestClose;

    public InstallationWizardViewModel(GameInstance game, OptiScalerVersion version)
    {
        _optiScalerService = new OptiScalerService();
        _options = new InstallationOptions
        {
            GamePath = game.GamePath,
            VersionPath = version.LocalPath
        };

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        IsCheckingEnvironment = true;

        // Check Engine
        if (Directory.Exists(Path.Combine(_options.GamePath, "Engine")))
        {
            ShowEngineWarning = true;
        }

        // Check Wine (Simple registry check mock or file check)
        // In .NET cross-platform, difficult to check registry easily without platform guards.
        // We'll assume Windows logic primarily as requested.
        _options.IsWine = CheckWine();
        IsWine = _options.IsWine;

        // Check GPU
        await CheckGpu();

        IsCheckingEnvironment = false;
        UpdateState();
    }

    private bool CheckWine()
    {
        // Simple heuristic: Z: drive mapping usually exists in Wine
        // Or check specific environment variables
        return Environment.GetEnvironmentVariable("WINEDLLPATH") != null;
    }

    private async Task CheckGpu()
    {
        if (IsWine)
        {
            GpuName = "Wine Environment (Skipping Detection)";
            IsNvidia = false; 
            EnableSpoofing = false; // "Skipping over spoofing checks" -> implies we assume no spoofing or user handles it?
            // Actually bat says: "Using wine, skipping over spoofing checks. If you need, you can disable spoofing..."
            // It skips the prompt but allows config edit. 
            // Let's default to True (Auto) but allow user to change.
            EnableSpoofing = true; 
            return;
        }

        // Check for nvapi64.dll as proxy for Nvidia
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (File.Exists(Path.Combine(system32, "nvapi64.dll")))
        {
            IsNvidia = true;
            GpuName = "Nvidia GPU Detected";
            EnableSpoofing = true; // "Skip spoofing if Nvidia" -> Bat creates config with Auto (default).
            // Actually bat logic:
            // if Nvidia -> completeSetup (Spoofing stays Auto/Default)
            // if AMD -> Ask user.
            // If user says "Yes" -> Auto. If "No" -> Dxgi=false.
            // So if Nvidia, we want EnableSpoofing = true (which leaves it as Auto).
        }
        else
        {
            IsNvidia = false;
            GpuName = "AMD/Intel GPU Detected";
            EnableSpoofing = true; // Default to yes, user can uncheck
        }
    }

    [RelayCommand]
    private async Task Next()
    {
        if (StepIndex == 0 && ShowEngineWarning)
        {
            // Just acknowledgment
        }

        if (StepIndex == 1) // Filename
        {
            var path = Path.Combine(_options.GamePath, SelectedFilename);
            if (File.Exists(path) && !FileExistsWarning)
            {
                FileExistsWarning = true;
                return; // Require second click to confirm
            }
            _options.TargetFilename = SelectedFilename;
        }

        if (StepIndex == 2) // GPU
        {
            _options.EnableSpoofing = EnableSpoofing;
            
            // Trigger OptiPatcher check for next step
            CheckingOptiPatcher = true;
            OptiPatcherStatus = "Checking GitHub for compatibility...";
            // Don't await here to allow UI transition, but we need result before showing step 3 fully?
            // Let's await it.
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
        
        if (StepIndex == 4) // Install
        {
             await Install();
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
            UpdateState();
        }
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateState()
    {
        CanGoBack = StepIndex > 0 && !IsInstalling && !InstallSuccess;
        
        // Notify visibility properties changed
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
        
        switch (StepIndex)
        {
            case 0: 
                Title = "Welcome"; 
                NextButtonText = "Next";
                break;
            case 1: 
                Title = "Select Filename"; 
                FileExistsWarning = false;
                break;
            case 2: 
                Title = "Configuration"; 
                break;
            case 3: 
                Title = "OptiPatcher"; 
                // Skip this step if checking failed or not relevant? 
                // The bat always checks.
                break;
            case 4: 
                Title = "Ready to Install"; 
                NextButtonText = "Install";
                break;
            case 5:
                Title = "Finished";
                CanGoBack = false;
                CanGoNext = false;
                break;
        }
    }

    private async Task Install()
    {
        IsInstalling = true;
        InstallStatus = "Installing OptiScaler...";
        _options.UseOptiPatcher = UseOptiPatcher;
        _options.CreateUninstaller = CreateUninstaller;

        try
        {
            await _optiScalerService.InstallAsync(_options);
            InstallSuccess = true;
            InstallStatus = "Installation Complete!";
            StepIndex++; // Go to finish
            UpdateState();
        }
        catch (Exception ex)
        {
            InstallStatus = $"Error: {ex.Message}";
            IsInstalling = false;
        }
    }
}
