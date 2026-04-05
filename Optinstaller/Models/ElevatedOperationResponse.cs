namespace Optinstaller.Models;

public sealed class ElevatedOperationResponse
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}
