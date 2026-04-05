using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optinstaller.Models;
using Optinstaller.Services;

namespace Optinstaller.ViewModels;

public partial class InstallationWizardViewModel : ViewModelBase
{
    private readonly OptiScalerService _optiScalerService;
    private readonly InstallationOptions _options;
    private readonly Task _initializationTask;

    [ObservableProperty] private int _stepIndex = 0;
    [ObservableProperty] private string _title = "Welcome";
    [ObservableProperty] private bool _canGoNext = true;
    [ObservableProperty] private bool _canGoBack = false;
    [ObservableProperty] private string _nextButtonText = "Next";

    [ObservableProperty] private bool _showEngineWarning;
    [ObservableProperty] private bool _isSupportedArchitecture = true;
    [ObservableProperty] private string _unsupportedArchitectureMessage = string.Empty;
    [ObservableProperty] private bool _requiresAdministratorAccess;
    [ObservableProperty] private bool _isCheckingEnvironment;
    
    [ObservableProperty] private ObservableCollection<OptiScalerVersion> _availableVersions;
    [ObservableProperty] private OptiScalerVersion? _selectedVersion;

    [ObservableProperty] private string _selectedFilename = "dxgi.dll";
    [ObservableProperty] private bool _fileExistsWarning;
    [ObservableProperty] private InstallTargetConflictInfo _selectedFilenameConflict = InstallTargetConflictInfo.None;
    
    [ObservableProperty] private bool _isNvidia;
    [ObservableProperty] private bool _enableSpoofing = true;
    [ObservableProperty] private bool _isWine;
    [ObservableProperty] private string _gpuName = "Detecting...";
    
    [ObservableProperty] private bool _checkingOptiPatcher;
    [ObservableProperty] 
    private bool _optiPatcherSupported;
    [ObservableProperty] private bool _useOptiPatcher;
    [ObservableProperty] private string _optiPatcherStatus = "Checking compatibility...";

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

    private string GameExecutablePath => string.IsNullOrWhiteSpace(_gameExecutableName)
        ? string.Empty
        : Path.Combine(_options.GamePath, _gameExecutableName);

    private readonly string _gameExecutableName;

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
        _gameExecutableName = game.ExecutableName;

        RefreshSelectedFilenameConflict();

        _initializationTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsCheckingEnvironment = true;

            if (Directory.Exists(Path.Combine(_options.GamePath, "Engine")))
            {
                ShowEngineWarning = true;
            }

            CheckExecutableArchitecture();
            RequiresAdministratorAccess = ElevatedOperationService.RequiresElevation(_options.GamePath);

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

    public async Task InstallWithDefaultsAsync()
    {
        if (IsInstalling)
        {
            return;
        }

        await _initializationTask;

        if (SelectedVersion == null)
        {
            throw new InvalidOperationException("Download an OptiScaler version before installing.");
        }

        _options.VersionPath = SelectedVersion.LocalPath;
        _options.TargetFilename = SelectedFilename;
        _options.EnableSpoofing = EnableSpoofing;
        ApplySelectedFilenameInstallBehavior();

        EnsureSupportedArchitecture();

        if (SelectedFilenameConflict.HasRiskyConflict && !SelectedFilenameConflict.HasChainedLoaderRecommendation)
        {
            throw new InvalidOperationException(BuildConflictInstallMessage(SelectedFilenameConflict));
        }

        if (SelectedFilenameConflict.RequiresAsiLoader && !SelectedFilenameConflict.HasDetectedAsiLoader)
        {
            throw new InvalidOperationException("OptiScaler.asi needs an existing ASI loader in the game folder. Open the full install wizard and choose another target filename unless you already know one is present.");
        }

        if (IsNvidia)
        {
            OptiPatcherSupported = false;
            UseOptiPatcher = false;
            OptiPatcherStatus = "OptiPatcher is skipped for Nvidia GPUs by default.";
        }
        else
        {
            await RefreshOptiPatcherSupportAsync();
        }

        StepIndex = 5;
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

        if (!InstallSuccess)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(InstallStatus)
                ? "OptiScaler installation did not complete."
                : InstallStatus);
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

        var detectedGpu = await Task.Run(() => GpuDetectionService.Detect());
        IsNvidia = detectedGpu.HasNvidia;
        GpuName = detectedGpu.Summary;
        EnableSpoofing = true;
    }

    [RelayCommand]
    public async Task Next()
    {
        if (IsInstalling) return;

        EnsureSupportedArchitecture();

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
            if (SelectedFilenameConflict.HasRiskyConflict && !SelectedFilenameConflict.HasChainedLoaderRecommendation && !FileExistsWarning)
            {
                FileExistsWarning = true;
                return;
            }
            _options.TargetFilename = SelectedFilename;
            ApplySelectedFilenameInstallBehavior();
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

            await RefreshOptiPatcherSupportAsync();
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
    public void Back()
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
    public void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void ForceOptiPatcher()
    {
        OptiPatcherSupported = true;
        UseOptiPatcher = true;
        OptiPatcherStatus = "Force installed enabled by user.";
    }

    private void UpdateState()
    {
        CanGoBack = StepIndex > 0 && !IsInstalling && !InstallSuccess;
        CanGoNext = !IsInstalling && IsSupportedArchitecture;
        
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

    private void CheckExecutableArchitecture()
    {
        if (string.IsNullOrWhiteSpace(GameExecutablePath) || !File.Exists(GameExecutablePath))
        {
            IsSupportedArchitecture = true;
            UnsupportedArchitectureMessage = string.Empty;
            return;
        }

        if (_optiScalerService.IsSupportedExecutableArchitecture(GameExecutablePath))
        {
            IsSupportedArchitecture = true;
            UnsupportedArchitectureMessage = string.Empty;
            return;
        }

        IsSupportedArchitecture = false;
        UnsupportedArchitectureMessage = $"{Path.GetFileName(GameExecutablePath)} appears to be 32-bit. OptiScaler only supports 64-bit games, so installation is blocked.";
    }

    private void EnsureSupportedArchitecture()
    {
        if (!IsSupportedArchitecture)
        {
            throw new InvalidOperationException(UnsupportedArchitectureMessage);
        }
    }

    private async Task RefreshOptiPatcherSupportAsync()
    {
        CheckingOptiPatcher = true;
        OptiPatcherStatus = "Checking GitHub for compatibility...";

        try
        {
            var supported = await _optiScalerService.CheckOptiPatcherSupportAsync(_options.GamePath);
            OptiPatcherSupported = supported;
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
        finally
        {
            CheckingOptiPatcher = false;
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

    partial void OnSelectedFilenameChanged(string value)
    {
        FileExistsWarning = false;
        RefreshSelectedFilenameConflict();
    }

    public void UseRecommendedFilename()
    {
        if (!SelectedFilenameConflict.HasRecommendedFilename)
        {
            return;
        }

        SelectedFilename = SelectedFilenameConflict.RecommendedFilename;
    }

    private void RefreshSelectedFilenameConflict()
    {
        SelectedFilenameConflict = _optiScalerService.AnalyzeTargetFilename(_options.GamePath, SelectedFilename);
    }

    private void ApplySelectedFilenameInstallBehavior()
    {
        _options.ChainedLoaderProvider = string.Empty;
        _options.ChainedLoaderSourceFilename = string.Empty;
        _options.ChainedLoaderDestinationFilename = string.Empty;
        _options.CreateSpecialKDxgiMarker = false;

        if (!SelectedFilenameConflict.HasChainedLoaderRecommendation)
        {
            return;
        }

        _options.ChainedLoaderProvider = SelectedFilenameConflict.ChainedLoaderProvider;
        _options.ChainedLoaderSourceFilename = SelectedFilenameConflict.ChainedLoaderSourceFilename;
        _options.ChainedLoaderDestinationFilename = SelectedFilenameConflict.ChainedLoaderDestinationFilename;
        _options.CreateSpecialKDxgiMarker = SelectedFilenameConflict.ChainedLoaderProvider.Equals("Special K", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConflictInstallMessage(InstallTargetConflictInfo conflict)
    {
        if (conflict.HasChainedLoaderRecommendation)
        {
            return conflict.ChainedLoaderInstructions;
        }

        var details = string.IsNullOrWhiteSpace(conflict.ExistingDetails)
            ? conflict.ExistingProvider
            : $"{conflict.ExistingProvider} ({conflict.ExistingDetails})";

        if (conflict.HasRecommendedFilename)
        {
            if (conflict.RecommendedFilename.Equals("OptiScaler.asi", StringComparison.OrdinalIgnoreCase) && conflict.HasDetectedAsiLoader)
            {
                var instructions = string.IsNullOrWhiteSpace(conflict.AsiLoaderInstructions)
                    ? string.Empty
                    : $" {conflict.AsiLoaderInstructions}";
                return $"{conflict.TargetFilename} is already used by {details}. Keep that loader in place and install OptiScaler as OptiScaler.asi instead.{instructions}";
            }

            return $"{conflict.TargetFilename} is already used by {details}. Choose {conflict.RecommendedFilename} instead of overwriting it.";
        }

        return $"{conflict.TargetFilename} is already used by {details}. Open the full install wizard if you intentionally want to overwrite it.";
    }
}
