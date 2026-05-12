namespace Harmonia.Host.Core;

public interface IProjectTrackerClient
{
    Task<ProjectItemsPage> GetProjectItemsAsync(
        TrackerConfig tracker,
        string? after,
        CancellationToken cancellationToken);

    Task<string?> GetIssueStatusAsync(
        TrackerConfig tracker,
        int issueNumber,
        CancellationToken cancellationToken);

    Task<string> ExecuteGraphQlAsync(
        TrackerConfig tracker,
        string query,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken);
}

public interface IProjectPoller
{
    Task<IReadOnlyCollection<ProjectItem>> GetActiveItemsAsync(
        TrackerConfig tracker,
        string priorityField,
        CancellationToken cancellationToken);
}

public interface IDependencyChecker
{
    Task<bool> IsBlockedAsync(
        TrackerConfig tracker,
        string issueBody,
        CancellationToken cancellationToken);
}

public interface IWorkspaceManager
{
    Task<string> EnsureWorkspaceAsync(string repositoryRoot, string issueIdentifier, CancellationToken cancellationToken);

    Task RemoveWorkspaceAsync(string repositoryRoot, string issueIdentifier, CancellationToken cancellationToken);
}

public interface IAgentEngine
{
    Task<AgentRunResultType> RunAsync(AgentExecutionContext context, CancellationToken cancellationToken);
}

public sealed record AgentExecutionContext(
    string WorkspacePath,
    string Prompt,
    string Model,
    int MaxTurns,
    int TurnTimeoutMs,
    int StallTimeoutMs,
    Func<string, IReadOnlyDictionary<string, object?>?, CancellationToken, Task<string>> GithubGraphQlTool);

public interface IAgentRunner
{
    Task<AgentRunResult> RunAsync(
        WorkflowDefinition workflow,
        ProjectItem item,
        string workspacePath,
        CancellationToken cancellationToken);
}

public interface IHookRunner
{
    Task RunAsync(string? command, string workingDirectory, int timeoutMs, CancellationToken cancellationToken);
}
