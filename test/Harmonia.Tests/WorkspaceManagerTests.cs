using System.Diagnostics;
using Harmonia.Host.Core;

namespace Harmonia.Tests;

public class WorkspaceManagerTests
{
    [Test]
    public async Task EnsureAndRemoveWorkspace_CreatesIdempotentlyAndRunsHooks()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"harmonia-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var workspacesPath = Path.Combine(tempRoot, "workspaces");

        Directory.CreateDirectory(repoPath);
        await RunGitAsync(repoPath, "init");
        await RunGitAsync(repoPath, "config user.email test@example.com");
        await RunGitAsync(repoPath, "config user.name Test User");
        await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "hello");
        await RunGitAsync(repoPath, "add README.md");
        await RunGitAsync(repoPath, "commit -m init");

        var hooks = new HooksConfig("after", null, null, "before-remove", 1000);
        var hookRunner = new FakeHookRunner();
        var manager = new WorkspaceManager(new WorkspaceConfig(workspacesPath), hooks, hookRunner);

        var firstPath = await manager.EnsureWorkspaceAsync(repoPath, "melkio/harmonia#42", CancellationToken.None);
        var secondPath = await manager.EnsureWorkspaceAsync(repoPath, "melkio/harmonia#42", CancellationToken.None);

        Assert.That(firstPath, Is.EqualTo(secondPath));
        Assert.That(Directory.Exists(firstPath), Is.True);
        Assert.That(Path.GetFileName(firstPath), Is.EqualTo("melkio_harmonia_42"));
        Assert.That(hookRunner.Calls.Count(call => call.Command == "after"), Is.EqualTo(1));

        await manager.RemoveWorkspaceAsync(repoPath, "melkio/harmonia#42", CancellationToken.None);

        Assert.That(Directory.Exists(firstPath), Is.False);
        Assert.That(hookRunner.Calls.Any(call => call.Command == "before-remove"), Is.True);

        Directory.Delete(tempRoot, recursive: true);
    }

    private static async Task RunGitAsync(string workingDirectory, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                ArgumentList = { "--no-pager" }
            }
        };

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"git {arguments} failed: {error}");
        }
    }
}
