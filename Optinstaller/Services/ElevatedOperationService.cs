using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Optinstaller.Models;

namespace Optinstaller.Services;

public static class ElevatedOperationService
{
    private const string ElevatedOperationArgument = "--elevated-operation";

    public static bool TryGetRequestPath(string[] args, out string requestPath)
    {
        requestPath = string.Empty;
        if (args.Length != 2 || !string.Equals(args[0], ElevatedOperationArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        requestPath = args[1];
        return !string.IsNullOrWhiteSpace(requestPath);
    }

    public static async Task<int> RunRequestAsync(string requestPath)
    {
        ElevatedOperationRequest? request = null;

        try
        {
            await using var requestStream = File.OpenRead(requestPath);
            request = await JsonSerializer.DeserializeAsync(requestStream, ElevatedOperationJsonContext.Default.ElevatedOperationRequest);
            if (request == null)
            {
                throw new InvalidOperationException("The elevated operation request could not be read.");
            }

            await new OptiScalerService().ExecuteElevatedOperationAsync(request);
            await WriteResponseAsync(request.ResponsePath, new ElevatedOperationResponse { Success = true });
            return 0;
        }
        catch (Exception ex)
        {
            if (request != null && !string.IsNullOrWhiteSpace(request.ResponsePath))
            {
                await WriteResponseAsync(request.ResponsePath, new ElevatedOperationResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                });
            }

            return 1;
        }
    }

    public static bool RequiresElevation(string directoryPath)
    {
        return OperatingSystem.IsWindows() &&
               !IsProcessElevated() &&
               !CanWriteToDirectory(directoryPath);
    }

    public static bool IsAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        return ex.HResult == unchecked((int)0x80070005);
    }

    public static async Task RunElevatedAsync(ElevatedOperationRequest request)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("Could not locate the current OptiManager executable to request administrator access.");
        }

        var requestPath = Path.Combine(Path.GetTempPath(), $"optimanager-elevated-{Guid.NewGuid():N}.json");
        var responsePath = Path.Combine(Path.GetTempPath(), $"optimanager-elevated-response-{Guid.NewGuid():N}.json");
        request.ResponsePath = responsePath;

        try
        {
            await using (var requestStream = File.Create(requestPath))
            {
                await JsonSerializer.SerializeAsync(requestStream, request, ElevatedOperationJsonContext.Default.ElevatedOperationRequest);
            }

            using var process = StartElevatedProcess(executablePath, requestPath);
            await process.WaitForExitAsync();

            var response = await ReadResponseAsync(responsePath);
            if (response?.Success == true)
            {
                return;
            }

            var errorMessage = response?.ErrorMessage;
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = process.ExitCode == 0
                    ? "The elevated operation did not report a result."
                    : "The elevated operation failed.";
            }

            throw new InvalidOperationException(errorMessage);
        }
        finally
        {
            TryDeleteFile(requestPath);
            TryDeleteFile(responsePath);
        }
    }

    private static Process StartElevatedProcess(string executablePath, string requestPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"{ElevatedOperationArgument} \"{requestPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
            });

            return process ?? throw new InvalidOperationException("Failed to start the elevated helper process.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Administrator access was required, but the UAC prompt was canceled.", ex);
        }
    }

    private static async Task WriteResponseAsync(string responsePath, ElevatedOperationResponse response)
    {
        await using var responseStream = File.Create(responsePath);
        await JsonSerializer.SerializeAsync(responseStream, response, ElevatedOperationJsonContext.Default.ElevatedOperationResponse);
    }

    private static async Task<ElevatedOperationResponse?> ReadResponseAsync(string responsePath)
    {
        if (!File.Exists(responsePath))
        {
            return null;
        }

        await using var responseStream = File.OpenRead(responsePath);
        return await JsonSerializer.DeserializeAsync(responseStream, ElevatedOperationJsonContext.Default.ElevatedOperationResponse);
    }

    private static bool CanWriteToDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        var probePath = Path.Combine(directoryPath, $".optimanager-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch
        {
            TryDeleteFile(probePath);
            return false;
        }
    }

    public static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
