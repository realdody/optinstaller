using System;
using System.Threading;
using Optinstaller.Platform;
using Optinstaller.Services;
using Optinstaller.UI;

namespace Optinstaller;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplyOverlayCompatibilityGuards();

        if (ElevatedOperationService.TryGetRequestPath(args, out var requestPath))
        {
            Environment.ExitCode = ElevatedOperationService.RunRequestAsync(requestPath).GetAwaiter().GetResult();
            return;
        }

        var syncContext = new UiSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        using var app = new OptinstallerImGuiApp(syncContext);
        app.Run();
    }

    private static void ApplyOverlayCompatibilityGuards()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Best-effort opt-out for newer RTSS builds so the overlay hook ignores this process.
        Environment.SetEnvironmentVariable("RTSSHooksCompatibility", "1", EnvironmentVariableTarget.Process);
    }
}
