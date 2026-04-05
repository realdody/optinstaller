namespace Optinstaller.Models;

public sealed class ElevatedOperationRequest
{
    public string Operation { get; set; } = string.Empty;

    public InstallationOptions Options { get; set; } = new();

    public string ResponsePath { get; set; } = string.Empty;
}
