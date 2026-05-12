namespace Harmonia.Host.Core;

public sealed class ProjectPoller(IProjectTrackerClient trackerClient) : IProjectPoller
{
    private static readonly IReadOnlyDictionary<string, int> DefaultPriorityOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Urgent"] = 1,
        ["High"] = 2,
        ["Medium"] = 3,
        ["Low"] = 4
    };

    public async Task<IReadOnlyCollection<ProjectItem>> GetActiveItemsAsync(
        TrackerConfig tracker,
        string priorityField,
        CancellationToken cancellationToken)
    {
        var items = new List<ProjectItem>();
        string? cursor = null;

        do
        {
            var page = await trackerClient.GetProjectItemsAsync(tracker, cursor, cancellationToken);
            items.AddRange(page.Items.Where(item => tracker.ActiveStates.Contains(item.Status, StringComparer.Ordinal)));
            cursor = page.HasNextPage ? page.EndCursor : null;
        }
        while (cursor is not null);

        return items
            .OrderBy(item => ResolvePriority(item.Priority))
            .ThenBy(item => item.IssueNumber)
            .ToArray();
    }

    private static int ResolvePriority(string? value)
    {
        if (value is null)
        {
            return int.MaxValue;
        }

        return DefaultPriorityOrder.TryGetValue(value, out var priority)
            ? priority
            : int.MaxValue;
    }
}
