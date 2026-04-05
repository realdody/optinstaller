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
}
