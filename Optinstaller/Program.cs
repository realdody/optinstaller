using System;
using System.Threading;
using Optinstaller.Platform;
using Optinstaller.UI;

namespace Optinstaller;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        _ = args;

        var syncContext = new UiSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        using var app = new OptinstallerImGuiApp(syncContext);
        app.Run();
    }
}
