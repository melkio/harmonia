using Harmonia.Host.Core;

namespace Harmonia.Tests;

public class OrchestratorTests
{
    private static readonly WorkflowDefinition Workflow = new(
        new TrackerConfig(
            "github_projects_v2",
            "token",
            "melkio",
            42,
            new[] { "Ready", "In Progress" },
            new[] { "Done" },
            "Priority",
            "In Review",
            "https://api.github.com/graphql"),
        new PollingConfig(1000),
        new AgentConfig(1, new Dictionary<string, int> { ["Ready"] = 1 }, 10_000, 5),
        new CopilotConfig("gpt-5", 1000, 2000),
        new WorkspaceConfig("/tmp/workspaces"),
        new HooksConfig(null, null, null, null, 1000),
        "prompt");

    [Test]
    public async Task PollOnceAsync_UnresolvedBlockers_SkipsDispatch()
    {
        var poller = new StubPoller([CreateItem(1)]);
        var dependency = new StubDependencyChecker(isBlocked: true);
        var runner = new RecordingAgentRunner();

        var orchestrator = new Orchestrator(
            Workflow,
            poller,
            dependency,
            new StubWorkspaceManager(),
            runner,
            new MutableTimeProvider(DateTimeOffset.UtcNow));

        await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);

        Assert.That(runner.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task PollOnceAsync_GlobalConcurrencyLimit_DispatchesOnlyAllowedCount()
    {
        var poller = new StubPoller([CreateItem(1), CreateItem(2)]);
        var dependency = new StubDependencyChecker(isBlocked: false);
        var runner = new RecordingAgentRunner(blockExecution: true);

        var orchestrator = new Orchestrator(
            Workflow,
            poller,
            dependency,
            new StubWorkspaceManager(),
            runner,
            new MutableTimeProvider(DateTimeOffset.UtcNow));

        await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);

        Assert.That(runner.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task PollOnceAsync_InFlightIssue_IsNotDispatchedAgain()
    {
        var item = CreateItem(1);
        var poller = new StubPoller([item]);
        var runner = new RecordingAgentRunner(blockExecution: true);

        var orchestrator = new Orchestrator(
            Workflow,
            poller,
            new StubDependencyChecker(isBlocked: false),
            new StubWorkspaceManager(),
            runner,
            new MutableTimeProvider(DateTimeOffset.UtcNow));

        await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);
        await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);

        Assert.That(runner.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task PollOnceAsync_FailedDispatch_IsNotImmediatelyRetried()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var item = CreateItem(1);
        var poller = new StubPoller([item]);
        var runner = new RecordingAgentRunner([AgentRunResultType.Error]);

        var orchestrator = new Orchestrator(
            Workflow,
            poller,
            new StubDependencyChecker(isBlocked: false),
            new StubWorkspaceManager(),
            runner,
            timeProvider);

        await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);
        await Task.Delay(50);
        await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);

        Assert.That(runner.Calls, Is.EqualTo(1));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 5 && runner.Calls < 2; i++)
        {
            await orchestrator.PollOnceAsync("/tmp/repo", CancellationToken.None);
            await Task.Delay(20);
        }

        Assert.That(runner.Calls, Is.EqualTo(2));
    }

    private static ProjectItem CreateItem(int number)
        => new($"repo#{number}", number, $"node-{number}", "Ready", "High", $"Issue {number}", "body");

    private sealed class StubPoller(IReadOnlyCollection<ProjectItem> items) : IProjectPoller
    {
        public Task<IReadOnlyCollection<ProjectItem>> GetActiveItemsAsync(TrackerConfig tracker, string priorityField, CancellationToken cancellationToken)
            => Task.FromResult(items);
    }

    private sealed class StubDependencyChecker(bool isBlocked) : IDependencyChecker
    {
        public Task<bool> IsBlockedAsync(TrackerConfig tracker, string issueBody, CancellationToken cancellationToken)
            => Task.FromResult(isBlocked);
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public Task<string> EnsureWorkspaceAsync(string repositoryRoot, string issueIdentifier, CancellationToken cancellationToken)
            => Task.FromResult(Path.Combine("/tmp", issueIdentifier.Replace('#', '_')));

        public Task RemoveWorkspaceAsync(string repositoryRoot, string issueIdentifier, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingAgentRunner : IAgentRunner
    {
        private readonly Queue<AgentRunResultType> _results;
        private readonly bool _blockExecution;

        public RecordingAgentRunner(IEnumerable<AgentRunResultType>? results = null, bool blockExecution = false)
        {
            _results = new Queue<AgentRunResultType>(results ?? new[] { AgentRunResultType.Success });
            _blockExecution = blockExecution;
        }

        public int Calls { get; private set; }

        public async Task<AgentRunResult> RunAsync(WorkflowDefinition workflow, ProjectItem item, string workspacePath, CancellationToken cancellationToken)
        {
            Calls++;

            if (_blockExecution)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var result = _results.Count > 0 ? _results.Dequeue() : AgentRunResultType.Success;
            return new AgentRunResult(result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
