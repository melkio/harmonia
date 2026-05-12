using System.Collections.Concurrent;

namespace Harmonia.Host.Core;

public sealed class Orchestrator(
    WorkflowDefinition workflow,
    IProjectPoller poller,
    IDependencyChecker dependencyChecker,
    IWorkspaceManager workspaceManager,
    IAgentRunner agentRunner,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, DispatchState> _dispatches = new(StringComparer.Ordinal);

    public async Task PollOnceAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var items = await poller.GetActiveItemsAsync(workflow.Tracker, workflow.Tracker.PriorityField, cancellationToken);

        var runningByState = _dispatches.Values
            .Where(state => state.IsRunning)
            .GroupBy(state => state.State, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var runningTotal = _dispatches.Values.Count(state => state.IsRunning);

        foreach (var item in items)
        {
            if (runningTotal >= workflow.Agent.MaxConcurrentAgents)
            {
                break;
            }

            if (_dispatches.TryGetValue(item.IssueIdentifier, out var current))
            {
                if (current.IsRunning)
                {
                    continue;
                }

                if (current.NextAttemptAtUtc > now)
                {
                    continue;
                }
            }

            if (workflow.Agent.MaxConcurrentAgentsByState.TryGetValue(item.Status, out var maxByState))
            {
                var runningForState = runningByState.TryGetValue(item.Status, out var value) ? value : 0;
                if (runningForState >= maxByState)
                {
                    continue;
                }
            }

            if (await dependencyChecker.IsBlockedAsync(workflow.Tracker, item.Body, cancellationToken))
            {
                continue;
            }

            var state = new DispatchState(item.Status, Attempt: (current?.Attempt ?? 0) + 1, IsRunning: true);
            _dispatches[item.IssueIdentifier] = state;
            _ = DispatchAsync(repositoryRoot, item, state.Attempt, cancellationToken);

            runningTotal++;
            runningByState[item.Status] = runningByState.TryGetValue(item.Status, out var running) ? running + 1 : 1;
        }
    }

    private async Task DispatchAsync(string repositoryRoot, ProjectItem item, int attempt, CancellationToken cancellationToken)
    {
        AgentRunResult result;

        try
        {
            var workspacePath = await workspaceManager.EnsureWorkspaceAsync(repositoryRoot, item.IssueIdentifier, cancellationToken);
            result = await agentRunner.RunAsync(workflow, item, workspacePath, cancellationToken);
        }
        catch (Exception)
        {
            result = new AgentRunResult(AgentRunResultType.Error);
        }

        if (result.Type == AgentRunResultType.Success)
        {
            _dispatches.TryRemove(item.IssueIdentifier, out _);
            return;
        }

        var nextDelay = CalculateBackoff(attempt, workflow.Agent.MaxRetryBackoffMs);
        var nextAttemptAt = _timeProvider.GetUtcNow().Add(nextDelay);

        _dispatches[item.IssueIdentifier] = new DispatchState(
            item.Status,
            attempt,
            IsRunning: false,
            NextAttemptAtUtc: nextAttemptAt);
    }

    private static TimeSpan CalculateBackoff(int attempt, int maxRetryBackoffMs)
    {
        var baseDelay = Math.Min(maxRetryBackoffMs, (int)Math.Pow(2, Math.Max(0, attempt - 1)) * 1000);
        return TimeSpan.FromMilliseconds(baseDelay);
    }

    private sealed record DispatchState(
        string State,
        int Attempt,
        bool IsRunning = false,
        DateTimeOffset NextAttemptAtUtc = default);
}
