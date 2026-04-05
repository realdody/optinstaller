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
        ApplyRtssCompatibilityGuards();

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

    private static void ApplyRtssCompatibilityGuards()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // The primary RTSS opt-out is injected into the apphost executable at build time.
        // Keep the process-local environment variable as a fallback for non-apphost launches.
        Environment.SetEnvironmentVariable("RTSSHooksCompatibility", "1", EnvironmentVariableTarget.Process);
    }
}
