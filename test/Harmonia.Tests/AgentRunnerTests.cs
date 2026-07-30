using Harmonia.Host.Core;

namespace Harmonia.Tests;

public class AgentRunnerTests
{
    private static readonly WorkflowDefinition Workflow = new(
        new TrackerConfig(
            "github_projects_v2",
            "token",
            "melkio",
            42,
            new[] { "Ready" },
            new[] { "Done" },
            "Priority",
            "In Review",
            "https://api.github.com/graphql"),
        new PollingConfig(1000),
        new AgentConfig(1, new Dictionary<string, int>(), 60000, 10),
        new CopilotConfig("gpt-5", 1000, 2000),
        new WorkspaceConfig("/tmp/workspaces"),
        new HooksConfig("after-create", "before-run", "after-run", "before-remove", 5000),
        "Issue {{ issue.identifier }} {{issue.title}} {{ issue.body }}");

    [Test]
    public async Task RunAsync_RendersPromptAndPassesThroughGraphQlTool()
    {
        var engine = new CapturingAgentEngine { NextResult = AgentRunResultType.TurnLimit };
        var trackerClient = new FakeProjectTrackerClient();
        var hooks = new FakeHookRunner();
        var runner = new AgentRunner(engine, trackerClient, hooks);

        var item = new ProjectItem("melkio/harmonia#42", 42, "node", "Ready", "High", "Fix bug", "Body text");

        var result = await runner.RunAsync(Workflow, item, "/tmp/workspace", CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(AgentRunResultType.TurnLimit));
        Assert.That(engine.LastContext, Is.Not.Null);
        Assert.That(engine.LastContext!.Prompt, Is.EqualTo("Issue melkio/harmonia#42 Fix bug Body text"));

        await engine.LastContext.GithubGraphQlTool("query Test", new Dictionary<string, object?> { ["n"] = 1 }, CancellationToken.None);
        Assert.That(trackerClient.ExecuteGraphQlCalls, Is.EqualTo(1));
        Assert.That(trackerClient.LastQuery, Is.EqualTo("query Test"));

        Assert.That(hooks.Calls.Select(call => call.Command), Is.EquivalentTo(new[] { "before-run", "after-run" }));
    }

    [Test]
    public async Task RunAsync_Cancellation_ReturnsStalled()
    {
        var engine = new CapturingAgentEngine { NextException = new OperationCanceledException() };
        var runner = new AgentRunner(engine, new FakeProjectTrackerClient(), new FakeHookRunner());

        var result = await runner.RunAsync(
            Workflow,
            new ProjectItem("repo#1", 1, "node", "Ready", "High", "Title", "Body"),
            "/tmp/workspace",
            CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(AgentRunResultType.Stalled));
    }
}
