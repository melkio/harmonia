using Harmonia.Host.Core;

namespace Harmonia.Tests;

internal sealed class FakeProjectTrackerClient : IProjectTrackerClient
{
    public Queue<ProjectItemsPage> Pages { get; } = new();

    public Dictionary<int, string?> IssueStatuses { get; } = new();

    public string LastQuery { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, object?>? LastVariables { get; private set; }

    public int ExecuteGraphQlCalls { get; private set; }

    public Task<ProjectItemsPage> GetProjectItemsAsync(TrackerConfig tracker, string? after, CancellationToken cancellationToken)
    {
        return Task.FromResult(Pages.Count > 0
            ? Pages.Dequeue()
            : new ProjectItemsPage(Array.Empty<ProjectItem>(), null, false));
    }

    public Task<string?> GetIssueStatusAsync(TrackerConfig tracker, int issueNumber, CancellationToken cancellationToken)
        => Task.FromResult(IssueStatuses.TryGetValue(issueNumber, out var status) ? status : null);

    public Task<string> ExecuteGraphQlAsync(TrackerConfig tracker, string query, IReadOnlyDictionary<string, object?>? variables, CancellationToken cancellationToken)
    {
        ExecuteGraphQlCalls++;
        LastQuery = query;
        LastVariables = variables;
        return Task.FromResult("{\"data\":{}}\n");
    }
}

internal sealed class FakeHookRunner : IHookRunner
{
    public List<(string? Command, string WorkingDirectory, int TimeoutMs)> Calls { get; } = [];

    public Task RunAsync(string? command, string workingDirectory, int timeoutMs, CancellationToken cancellationToken)
    {
        Calls.Add((command, workingDirectory, timeoutMs));
        return Task.CompletedTask;
    }
}

internal sealed class CapturingAgentEngine : IAgentEngine
{
    public AgentExecutionContext? LastContext { get; private set; }

    public AgentRunResultType NextResult { get; set; } = AgentRunResultType.Success;

    public Exception? NextException { get; set; }

    public Task<AgentRunResultType> RunAsync(AgentExecutionContext context, CancellationToken cancellationToken)
    {
        LastContext = context;

        if (NextException is not null)
        {
            throw NextException;
        }

        return Task.FromResult(NextResult);
    }
}
