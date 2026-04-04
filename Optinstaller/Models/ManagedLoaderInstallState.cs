namespace Optinstaller.Models;

public sealed class ManagedLoaderInstallState
{
    public string Provider { get; set; } = string.Empty;

    public string OriginalFilename { get; set; } = string.Empty;

    public string RedirectedFilename { get; set; } = string.Empty;

    public bool CreatedSpecialKDxgiMarker { get; set; }
}
