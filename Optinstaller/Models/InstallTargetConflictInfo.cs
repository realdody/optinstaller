namespace Optinstaller.Models;

public sealed class InstallTargetConflictInfo
{
    public static InstallTargetConflictInfo None { get; } = new();

    public string TargetFilename { get; init; } = string.Empty;

    public bool FileExists { get; init; }

    public bool IsOptiScaler { get; init; }

    public string ExistingProvider { get; init; } = string.Empty;

    public string ExistingDetails { get; init; } = string.Empty;

    public string RecommendedFilename { get; init; } = string.Empty;

    public string AsiLoaderProvider { get; init; } = string.Empty;

    public string AsiLoaderInstructions { get; init; } = string.Empty;

    public string ChainedLoaderProvider { get; init; } = string.Empty;

    public string ChainedLoaderSourceFilename { get; init; } = string.Empty;

    public string ChainedLoaderDestinationFilename { get; init; } = string.Empty;

    public string ChainedLoaderInstructions { get; init; } = string.Empty;

    public bool RequiresAsiLoader => TargetFilename.Equals("OptiScaler.asi", System.StringComparison.OrdinalIgnoreCase);

    public bool HasRiskyConflict => FileExists && !IsOptiScaler;

    public bool HasRecommendedFilename => !string.IsNullOrWhiteSpace(RecommendedFilename);

    public bool HasDetectedAsiLoader => !string.IsNullOrWhiteSpace(AsiLoaderProvider);

    public bool HasChainedLoaderRecommendation => !string.IsNullOrWhiteSpace(ChainedLoaderProvider);

    public bool ShouldPreferAsiInstall =>
        !HasChainedLoaderRecommendation &&
        HasDetectedAsiLoader &&
        RecommendedFilename.Equals("OptiScaler.asi", System.StringComparison.OrdinalIgnoreCase) &&
        !TargetFilename.Equals("OptiScaler.asi", System.StringComparison.OrdinalIgnoreCase);
}
