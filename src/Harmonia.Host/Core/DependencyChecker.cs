using System.Text.RegularExpressions;

namespace Harmonia.Host.Core;

public sealed partial class DependencyChecker(IProjectTrackerClient trackerClient) : IDependencyChecker
{
    private const string Header = "## Blocked by";

    [GeneratedRegex("^- \\[ \\] #(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockerRegex();

    public async Task<bool> IsBlockedAsync(
        TrackerConfig tracker,
        string issueBody,
        CancellationToken cancellationToken)
    {
        var blockers = ParseBlockerNumbers(issueBody);
        if (blockers.Count == 0)
        {
            return false;
        }

        foreach (var blocker in blockers)
        {
            var status = await trackerClient.GetIssueStatusAsync(tracker, blocker, cancellationToken);
            if (status is null || !tracker.TerminalStates.Contains(status, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyCollection<int> ParseBlockerNumbers(string issueBody)
    {
        if (string.IsNullOrEmpty(issueBody))
        {
            return Array.Empty<int>();
        }

        var lines = issueBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headerIndex = Array.FindIndex(lines, line => line == Header);
        if (headerIndex < 0)
        {
            return Array.Empty<int>();
        }

        var blockers = new List<int>();
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            var match = BlockerRegex().Match(line);
            if (match.Success)
            {
                blockers.Add(int.Parse(match.Groups[1].Value));
            }
        }

        return blockers;
    }
}
