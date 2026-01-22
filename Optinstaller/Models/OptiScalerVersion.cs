using System;

namespace Optinstaller.Models;

public class OptiScalerVersion
{
    public string Name { get; set; } = string.Empty; // e.g., "v0.6.5"
    public string TagName { get; set; } = string.Empty; // e.g., "v0.6.5"
    public string Description { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public bool IsDownloaded { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
}
