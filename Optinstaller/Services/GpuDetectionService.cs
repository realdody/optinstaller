using System;
using System.Collections.Generic;
using System.Linq;
using Optinstaller.Models;
using Vortice.DXGI;

namespace Optinstaller.Services;

public static class GpuDetectionService
{
    private const uint NvidiaVendorId = 0x10DE;
    private const uint AmdVendorId = 0x1002;
    private const uint IntelVendorId = 0x8086;

    private static readonly object CacheLock = new();
    private static GpuDetectionResult? _cachedResult;

    public static GpuDetectionResult Detect(bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            lock (CacheLock)
            {
                if (_cachedResult != null)
                {
                    return _cachedResult;
                }
            }
        }

        var result = DetectCore();
        lock (CacheLock)
        {
            _cachedResult = result;
        }

        return result;
    }

    private static GpuDetectionResult DetectCore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return CreateResult(Array.Empty<GpuAdapterInfo>());
        }

        var adapters = new List<GpuAdapterInfo>();
        var seenAdapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                var enumerationResult = factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter);
                if (enumerationResult.Failure || adapter == null)
                {
                    break;
                }

                using (adapter)
                {
                    var description = adapter.Description1;
                    if ((description.Flags & AdapterFlags.Software) == AdapterFlags.Software)
                    {
                        continue;
                    }

                    var adapterName = NormalizeAdapterName(description.Description);
                    if (string.IsNullOrWhiteSpace(adapterName))
                    {
                        continue;
                    }

                    var adapterKey = $"{description.VendorId}:{description.DeviceId}:{adapterName}";
                    if (!seenAdapters.Add(adapterKey))
                    {
                        continue;
                    }

                    adapters.Add(new GpuAdapterInfo(
                        adapterName,
                        description.VendorId,
                        description.DeviceId,
                        description.DedicatedVideoMemory));
                }
            }
        }
        catch
        {
        }

        var orderedAdapters = adapters
            .OrderByDescending(adapter => adapter.DedicatedVideoMemory)
            .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return CreateResult(orderedAdapters);
    }

    private static GpuDetectionResult CreateResult(IReadOnlyList<GpuAdapterInfo> adapters)
    {
        var hasNvidia = adapters.Any(adapter => adapter.VendorId == NvidiaVendorId);
        var hasAmd = adapters.Any(adapter => adapter.VendorId == AmdVendorId);
        var hasIntel = adapters.Any(adapter => adapter.VendorId == IntelVendorId);
        var summary = adapters.Count == 0
            ? "GPU detection unavailable"
            : string.Join(" + ", adapters.Select(adapter => adapter.Name));

        return new GpuDetectionResult(adapters, hasNvidia, hasAmd, hasIntel, summary);
    }

    private static string NormalizeAdapterName(string? adapterName)
    {
        return string.IsNullOrWhiteSpace(adapterName)
            ? string.Empty
            : adapterName.Trim();
    }
}
