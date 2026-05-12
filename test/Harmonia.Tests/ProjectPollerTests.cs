using Harmonia.Host.Core;

namespace Harmonia.Tests;

public class ProjectPollerTests
{
    private static readonly TrackerConfig Tracker = new(
        "github_projects_v2",
        "token",
        "melkio",
        42,
        new[] { "Ready", "In Progress" },
        new[] { "Done" },
        "Priority",
        "In Review",
        "https://api.github.com/graphql");

    [Test]
    public async Task GetActiveItemsAsync_PaginatesAndSortsByPriority()
    {
        var client = new FakeProjectTrackerClient();
        client.Pages.Enqueue(new ProjectItemsPage(
            [
                new ProjectItem("repo#2", 2, "n2", "Ready", "Low", "two", "body"),
                new ProjectItem("repo#3", 3, "n3", "Ready", "Urgent", "three", "body")
            ],
            "cursor-1",
            true));

        client.Pages.Enqueue(new ProjectItemsPage(
            [
                new ProjectItem("repo#4", 4, "n4", "In Progress", "Unknown", "four", "body"),
                new ProjectItem("repo#1", 1, "n1", "Done", "High", "one", "body")
            ],
            null,
            false));

        var poller = new ProjectPoller(client);

        var items = await poller.GetActiveItemsAsync(Tracker, Tracker.PriorityField, CancellationToken.None);

        Assert.That(items.Select(i => i.IssueNumber), Is.EqualTo(new[] { 3, 2, 4 }));
    }

    [Test]
    public async Task GetActiveItemsAsync_EmptyBoard_ReturnsEmpty()
    {
        var client = new FakeProjectTrackerClient();
        client.Pages.Enqueue(new ProjectItemsPage(Array.Empty<ProjectItem>(), null, false));

        var poller = new ProjectPoller(client);

        var items = await poller.GetActiveItemsAsync(Tracker, Tracker.PriorityField, CancellationToken.None);

        Assert.That(items, Is.Empty);
    }
}
