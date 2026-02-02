using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Optinstaller.Models;

public partial class OptiScalerVersion : ObservableObject
{
    public string Name { get; set; } = string.Empty; // e.g., "v0.6.5"
    public string TagName { get; set; } = string.Empty; // e.g., "v0.6.5"
    public string Description { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Source { get; set; } = "Official"; // "Official" or "BleedingEdge"
    
    // Computed property for UI binding
    public bool IsBleedingEdge => Source == "BleedingEdge";

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    // Computed property for display
    public string FileSizeDisplay => FileSize > 0 
        ? FileSize >= 1_048_576 
            ? $"{FileSize / 1_048_576.0:F1} MB" 
            : $"{FileSize / 1024.0:F1} KB"
        : string.Empty;

    // Computed property for relative time
    public string RelativeTime
    {
        get
        {
            var diff = DateTime.UtcNow - PublishedAt;
            if (diff.TotalDays >= 365) return $"{(int)(diff.TotalDays / 365)} year(s) ago";
            if (diff.TotalDays >= 30) return $"{(int)(diff.TotalDays / 30)} month(s) ago";
            if (diff.TotalDays >= 7) return $"{(int)(diff.TotalDays / 7)} week(s) ago";
            if (diff.TotalDays >= 1) return $"{(int)diff.TotalDays} day(s) ago";
            if (diff.TotalHours >= 1) return $"{(int)diff.TotalHours} hour(s) ago";
            return "Just now";
        }
    }
}
