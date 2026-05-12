using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Harmonia.Host.Core;

public sealed class GitHubProjectTrackerClient(HttpClient httpClient) : IProjectTrackerClient
{
    private const string ProjectItemsQuery = """
        query($owner: String!, $projectNumber: Int!, $after: String) {
          organization(login: $owner) {
            projectV2(number: $projectNumber) {
              items(first: 50, after: $after) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  id
                  content {
                    ... on Issue {
                      number
                      title
                      body
                      repository { nameWithOwner }
                    }
                  }
                  fieldValues(first: 50) {
                    nodes {
                      ... on ProjectV2ItemFieldSingleSelectValue {
                        name
                        field { ... on ProjectV2FieldCommon { name } }
                      }
                    }
                  }
                }
              }
            }
          }
          user(login: $owner) {
            projectV2(number: $projectNumber) {
              items(first: 50, after: $after) {
                pageInfo { hasNextPage endCursor }
                nodes {
                  id
                  content {
                    ... on Issue {
                      number
                      title
                      body
                      repository { nameWithOwner }
                    }
                  }
                  fieldValues(first: 50) {
                    nodes {
                      ... on ProjectV2ItemFieldSingleSelectValue {
                        name
                        field { ... on ProjectV2FieldCommon { name } }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    public async Task<ProjectItemsPage> GetProjectItemsAsync(
        TrackerConfig tracker,
        string? after,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object?>
        {
            ["owner"] = tracker.Owner,
            ["projectNumber"] = tracker.ProjectNumber,
            ["after"] = after
        };

        var json = await ExecuteGraphQlAsync(tracker, ProjectItemsQuery, variables, cancellationToken);
        using var document = JsonDocument.Parse(json);

        var project = GetProjectNode(document.RootElement);
        if (project.ValueKind == JsonValueKind.Undefined)
        {
            return new ProjectItemsPage(Array.Empty<ProjectItem>(), null, false);
        }

        var items = project.GetProperty("items");
        var nodes = items.GetProperty("nodes");

        var parsedItems = new List<ProjectItem>();
        foreach (var node in nodes.EnumerateArray())
        {
            if (!TryParseIssue(node, tracker.PriorityField, out var item))
            {
                continue;
            }

            parsedItems.Add(item);
        }

        var pageInfo = items.GetProperty("pageInfo");
        var hasNext = pageInfo.GetProperty("hasNextPage").GetBoolean();
        var endCursor = pageInfo.GetProperty("endCursor").ValueKind == JsonValueKind.Null
            ? null
            : pageInfo.GetProperty("endCursor").GetString();

        return new ProjectItemsPage(parsedItems, endCursor, hasNext);
    }

    public async Task<string?> GetIssueStatusAsync(
        TrackerConfig tracker,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        string? cursor = null;

        do
        {
            var page = await GetProjectItemsAsync(tracker, cursor, cancellationToken);
            var match = page.Items.FirstOrDefault(item => item.IssueNumber == issueNumber);
            if (match is not null)
            {
                return match.Status;
            }

            cursor = page.HasNextPage ? page.EndCursor : null;
        }
        while (cursor is not null);

        return null;
    }

    public async Task<string> ExecuteGraphQlAsync(
        TrackerConfig tracker,
        string query,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tracker.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tracker.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = JsonSerializer.Serialize(new
        {
            query,
            variables = variables ?? new Dictionary<string, object?>()
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        return content;
    }

    private static JsonElement GetProjectNode(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            return default;
        }

        if (data.TryGetProperty("organization", out var organization) &&
            organization.ValueKind != JsonValueKind.Null &&
            organization.TryGetProperty("projectV2", out var orgProject) &&
            orgProject.ValueKind != JsonValueKind.Null)
        {
            return orgProject;
        }

        if (data.TryGetProperty("user", out var user) &&
            user.ValueKind != JsonValueKind.Null &&
            user.TryGetProperty("projectV2", out var userProject) &&
            userProject.ValueKind != JsonValueKind.Null)
        {
            return userProject;
        }

        return default;
    }

    private static bool TryParseIssue(JsonElement node, string priorityField, out ProjectItem item)
    {
        item = default!;

        if (!node.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (!content.TryGetProperty("number", out var number) ||
            !content.TryGetProperty("title", out var title) ||
            !content.TryGetProperty("body", out var body))
        {
            return false;
        }

        var status = string.Empty;
        string? priority = null;

        if (node.TryGetProperty("fieldValues", out var fieldValues) &&
            fieldValues.TryGetProperty("nodes", out var valueNodes))
        {
            foreach (var valueNode in valueNodes.EnumerateArray())
            {
                if (!valueNode.TryGetProperty("field", out var field) ||
                    !field.TryGetProperty("name", out var fieldName) ||
                    !valueNode.TryGetProperty("name", out var valueName))
                {
                    continue;
                }

                var fieldNameText = fieldName.GetString();
                if (fieldNameText == "Status")
                {
                    status = valueName.GetString() ?? string.Empty;
                }

                if (fieldNameText == priorityField)
                {
                    priority = valueName.GetString();
                }
            }
        }

        var repoName = content.GetProperty("repository").GetProperty("nameWithOwner").GetString() ?? "unknown/unknown";
        var issueNumber = number.GetInt32();

        item = new ProjectItem(
            $"{repoName}#{issueNumber}",
            issueNumber,
            node.GetProperty("id").GetString() ?? string.Empty,
            status,
            priority,
            title.GetString() ?? string.Empty,
            body.GetString() ?? string.Empty);

        return true;
    }
}
