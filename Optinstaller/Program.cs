using System;
using System.Diagnostics;
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
        if (!EnsureRtssCompatibilityContext(args))
        {
            return;
        }

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

    private static bool EnsureRtssCompatibilityContext(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        const string rtssHooksCompatibilityVariable = "RTSSHooksCompatibility";

        if (string.Equals(
                Environment.GetEnvironmentVariable(rtssHooksCompatibilityVariable),
                "1",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (TryRelaunchWithRtssCompatibility(args, rtssHooksCompatibilityVariable))
        {
            return false;
        }

        // Fallback when relaunch is unavailable. This is less reliable because the variable is being
        // added after process creation, but it still helps on RTSS builds that check it later.
        Environment.SetEnvironmentVariable(rtssHooksCompatibilityVariable, "1", EnvironmentVariableTarget.Process);
        return true;
    }

    private static bool TryRelaunchWithRtssCompatibility(string[] args, string environmentVariableName)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            startInfo.EnvironmentVariables[environmentVariableName] = "1";
            using var childProcess = Process.Start(startInfo);
            return childProcess is not null;
        }
        catch
        {
            return false;
        }
    }
}
