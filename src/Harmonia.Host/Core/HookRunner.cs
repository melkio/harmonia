using System.Diagnostics;

namespace Harmonia.Host.Core;

public sealed class HookRunner : IHookRunner
{
    public async Task RunAsync(string? command, string workingDirectory, int timeoutMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
        var shellArgument = OperatingSystem.IsWindows() ? "/c" : "-lc";

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            ArgumentList = { shellArgument, command },
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Hook failed with exit code {process.ExitCode}: {error}");
        }
    }
}
