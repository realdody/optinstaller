using System.Collections.Generic;

namespace Optinstaller.Models;

public sealed record UpscalerComponentDetection(
    string Label,
    string FileName,
    string Version,
    bool IsDetected,
    string RelativePath);

public sealed record UpscalerDetectionResult(
    string SearchRootPath,
    IReadOnlyList<UpscalerComponentDetection> Components,
    bool HasSupportedComponents,
    string Summary);
