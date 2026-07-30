using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Harmonia.Host.Core;

public sealed class WorkspaceManager(
    WorkspaceConfig workspace,
    HooksConfig hooks,
    IHookRunner hookRunner) : IWorkspaceManager
{
    public async Task<string> EnsureWorkspaceAsync(string repositoryRoot, string issueIdentifier, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workspace.Root);

        var workspacePath = GetWorkspacePath(issueIdentifier);
        if (Directory.Exists(workspacePath))
        {
            return workspacePath;
        }

        await RunGitAsync(repositoryRoot, ["worktree", "add", workspacePath, "--detach"], cancellationToken);
        await hookRunner.RunAsync(hooks.AfterCreate, workspacePath, hooks.TimeoutMs, cancellationToken);

        return workspacePath;
    }

    public async Task RemoveWorkspaceAsync(string repositoryRoot, string issueIdentifier, CancellationToken cancellationToken)
    {
        var workspacePath = GetWorkspacePath(issueIdentifier);
        if (!Directory.Exists(workspacePath))
        {
            return;
        }

        await hookRunner.RunAsync(hooks.BeforeRemove, workspacePath, hooks.TimeoutMs, cancellationToken);
        await RunGitAsync(repositoryRoot, ["worktree", "remove", "--force", workspacePath], cancellationToken);

        if (Directory.Exists(workspacePath))
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    internal string GetWorkspacePath(string issueIdentifier)
    {
        var sanitized = Regex.Replace(issueIdentifier, "[^A-Za-z0-9]+", "_").Trim('_');
        return Path.GetFullPath(Path.Combine(workspace.Root, sanitized));
    }

    private static async Task RunGitAsync(string repositoryRoot, IReadOnlyCollection<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { "--no-pager" }
        };

        foreach (var part in arguments)
        {
            startInfo.ArgumentList.Add(part);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed: {error}");
        }
    }
}
