using Harmonia.Host.Core;

namespace Harmonia.Tests;

public class WorkflowLoaderTests
{
    [Test]
    public void Parse_ValidFrontMatter_ReturnsTypedConfigurationAndTemplate()
    {
        const string workflow = """
            ---
            tracker:
              kind: github_projects_v2
              api_key: token
              owner: melkio
              project_number: 42
              active_states: [Ready, In Progress]
              terminal_states:
                - Done
                - Cancelled
              priority_field: Priority
            polling:
              interval_ms: 15000
            agent:
              max_concurrent_agents: 2
              max_concurrent_agents_by_state: { Ready: 1 }
              max_retry_backoff_ms: 120000
              max_turns: 25
            copilot:
              model: gpt-5
              turn_timeout_ms: 2000
              stall_timeout_ms: 3000
            workspace:
              root: /tmp/workspaces
            hooks:
              timeout_ms: 5000
              after_create: echo created
            ---
            Work on {{ issue.identifier }}: {{ issue.title }}
            """;

        var definition = WorkflowLoader.Parse(workflow);

        Assert.That(definition.Tracker.ProjectNumber, Is.EqualTo(42));
        Assert.That(definition.Tracker.ActiveStates, Is.EquivalentTo(new[] { "Ready", "In Progress" }));
        Assert.That(definition.Agent.MaxConcurrentAgentsByState["Ready"], Is.EqualTo(1));
        Assert.That(definition.Copilot.Model, Is.EqualTo("gpt-5"));
        Assert.That(definition.PromptTemplate.Trim(), Is.EqualTo("Work on {{ issue.identifier }}: {{ issue.title }}"));
    }

    [Test]
    public void Parse_MissingRequiredFields_ThrowsValidationExceptionWithMissingNames()
    {
        const string workflow = """
            ---
            tracker:
              kind: github_projects_v2
            ---
            body
            """;

        var exception = Assert.Throws<WorkflowValidationException>(() => WorkflowLoader.Parse(workflow));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.MissingFields, Does.Contain("tracker.api_key"));
        Assert.That(exception.MissingFields, Does.Contain("tracker.owner"));
        Assert.That(exception.MissingFields, Does.Contain("tracker.project_number"));
    }

    [Test]
    public void Parse_InvalidYaml_ThrowsParseError()
    {
        const string workflow = """
            ---
            tracker
              kind: github_projects_v2
            ---
            body
            """;

        Assert.Throws<FormatException>(() => WorkflowLoader.Parse(workflow));
    }

    [Test]
    public void Parse_AppliesDefaults()
    {
        const string workflow = """
            ---
            tracker:
              kind: github_projects_v2
              api_key: token
              owner: melkio
              project_number: 42
            ---
            body
            """;

        var definition = WorkflowLoader.Parse(workflow);

        Assert.That(definition.Tracker.HandoffStatus, Is.EqualTo("In Review"));
        Assert.That(definition.Tracker.Endpoint, Is.EqualTo("https://api.github.com/graphql"));
        Assert.That(definition.Polling.IntervalMs, Is.EqualTo(10000));
    }
}
