using System.Collections.Generic;

namespace Optinstaller.Models;

public sealed record GpuAdapterInfo(
    string Name,
    uint VendorId,
    uint DeviceId,
    ulong DedicatedVideoMemory);

public sealed record GpuDetectionResult(
    IReadOnlyList<GpuAdapterInfo> Adapters,
    bool HasNvidia,
    bool HasAmd,
    bool HasIntel,
    string Summary);
