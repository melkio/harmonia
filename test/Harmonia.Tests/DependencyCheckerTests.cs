using Harmonia.Host.Core;

namespace Harmonia.Tests;

public class DependencyCheckerTests
{
    private static readonly TrackerConfig Tracker = new(
        "github_projects_v2",
        "token",
        "melkio",
        42,
        new[] { "Ready" },
        new[] { "Done", "Cancelled" },
        "Priority",
        "In Review",
        "https://api.github.com/graphql");

    [Test]
    public async Task IsBlockedAsync_NoBlockedBySection_ReturnsFalse()
    {
        var checker = new DependencyChecker(new FakeProjectTrackerClient());

        var blocked = await checker.IsBlockedAsync(Tracker, "No blockers here", CancellationToken.None);

        Assert.That(blocked, Is.False);
    }

    [Test]
    public async Task IsBlockedAsync_AllBlockersTerminal_ReturnsFalse()
    {
        var client = new FakeProjectTrackerClient();
        client.IssueStatuses[10] = "Done";
        client.IssueStatuses[11] = "Cancelled";
        var checker = new DependencyChecker(client);

        var body = """
            ## Blocked by
            - [ ] #10
            - [ ] #11
            """;

        var blocked = await checker.IsBlockedAsync(Tracker, body, CancellationToken.None);

        Assert.That(blocked, Is.False);
    }

    [Test]
    public async Task IsBlockedAsync_AnyNonTerminalBlocker_ReturnsTrue()
    {
        var client = new FakeProjectTrackerClient();
        client.IssueStatuses[10] = "Done";
        client.IssueStatuses[11] = "Ready";
        var checker = new DependencyChecker(client);

        var body = """
            ## Blocked by
            - [ ] #10
            - [ ] #11
            """;

        var blocked = await checker.IsBlockedAsync(Tracker, body, CancellationToken.None);

        Assert.That(blocked, Is.True);
    }

    [Test]
    public void ParseBlockerNumbers_MalformedLine_IsIgnored()
    {
        var body = """
            ## Blocked by
            - [] #10
            """;

        var blockers = DependencyChecker.ParseBlockerNumbers(body);

        Assert.That(blockers, Is.Empty);
    }
}
