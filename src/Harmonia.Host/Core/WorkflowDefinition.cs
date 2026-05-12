namespace Harmonia.Host.Core;

public sealed record WorkflowDefinition(
    TrackerConfig Tracker,
    PollingConfig Polling,
    AgentConfig Agent,
    CopilotConfig Copilot,
    WorkspaceConfig Workspace,
    HooksConfig Hooks,
    string PromptTemplate);

public sealed record TrackerConfig(
    string Kind,
    string ApiKey,
    string Owner,
    int ProjectNumber,
    IReadOnlyCollection<string> ActiveStates,
    IReadOnlyCollection<string> TerminalStates,
    string PriorityField,
    string HandoffStatus,
    string Endpoint);

public sealed record PollingConfig(int IntervalMs);

public sealed record AgentConfig(
    int MaxConcurrentAgents,
    IReadOnlyDictionary<string, int> MaxConcurrentAgentsByState,
    int MaxRetryBackoffMs,
    int MaxTurns);

public sealed record CopilotConfig(string Model, int TurnTimeoutMs, int StallTimeoutMs);

public sealed record WorkspaceConfig(string Root);

public sealed record HooksConfig(
    string? AfterCreate,
    string? BeforeRun,
    string? AfterRun,
    string? BeforeRemove,
    int TimeoutMs);

public sealed record ProjectItem(
    string IssueIdentifier,
    int IssueNumber,
    string NodeId,
    string Status,
    string? Priority,
    string Title,
    string Body);

public sealed record ProjectItemsPage(
    IReadOnlyCollection<ProjectItem> Items,
    string? EndCursor,
    bool HasNextPage);

public sealed record AgentRunResult(AgentRunResultType Type, string? Error = null);

public enum AgentRunResultType
{
    Success,
    Stalled,
    TurnLimit,
    Error
}

public sealed class WorkflowValidationException(IReadOnlyCollection<string> missingFields)
    : Exception($"Missing required workflow fields: {string.Join(", ", missingFields)}")
{
    public IReadOnlyCollection<string> MissingFields { get; } = missingFields;
}
