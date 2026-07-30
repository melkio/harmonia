using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Harmonia.Host.Core;

public sealed class WorkflowLoader : IDisposable
{
    private readonly string _workflowPath;
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;
    private WorkflowDefinition? _current;

    public WorkflowLoader(string workflowPath)
    {
        _workflowPath = workflowPath;
    }

    public WorkflowDefinition Current => _current ?? throw new InvalidOperationException("Workflow has not been loaded yet.");

    public event EventHandler<WorkflowDefinition>? WorkflowReloaded;

    public async Task<WorkflowDefinition> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(_workflowPath, cancellationToken);
        var parsed = Parse(content);

        lock (_sync)
        {
            _current = parsed;
        }

        return parsed;
    }

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_workflowPath);
        var fileName = Path.GetFileName(_workflowPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnWorkflowChanged;
        _watcher.Created += OnWorkflowChanged;
        _watcher.Renamed += OnWorkflowChanged;
    }

    private async void OnWorkflowChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!File.Exists(_workflowPath))
            {
                return;
            }

            var content = await File.ReadAllTextAsync(_workflowPath);
            var parsed = Parse(content);

            lock (_sync)
            {
                _current = parsed;
            }

            WorkflowReloaded?.Invoke(this, parsed);
        }
        catch
        {
            // Ignore reload errors and keep previous valid snapshot.
        }
    }

    public static WorkflowDefinition Parse(string content)
    {
        const string separator = "---";

        if (!content.StartsWith(separator + "\n", StringComparison.Ordinal) &&
            !content.StartsWith(separator + "\r\n", StringComparison.Ordinal))
        {
            throw new FormatException("WORKFLOW.md must start with YAML front matter delimited by '---'.");
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var markerIndex = normalized.IndexOf("\n---\n", separator.Length, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new FormatException("WORKFLOW.md front matter is not closed with '---'.");
        }

        var frontMatter = normalized.Substring(separator.Length + 1, markerIndex - separator.Length - 1);
        var body = normalized[(markerIndex + "\n---\n".Length)..];

        var root = ParseYaml(frontMatter);

        var tracker = BuildTracker(GetSection(root, "tracker"));
        var polling = BuildPolling(GetSection(root, "polling"));
        var agent = BuildAgent(GetSection(root, "agent"));
        var copilot = BuildCopilot(GetSection(root, "copilot"));
        var workspace = BuildWorkspace(GetSection(root, "workspace"));
        var hooks = BuildHooks(GetSection(root, "hooks"));

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(tracker.Kind)) missing.Add("tracker.kind");
        if (string.IsNullOrWhiteSpace(tracker.ApiKey)) missing.Add("tracker.api_key");
        if (string.IsNullOrWhiteSpace(tracker.Owner)) missing.Add("tracker.owner");
        if (tracker.ProjectNumber <= 0) missing.Add("tracker.project_number");

        if (missing.Count > 0)
        {
            throw new WorkflowValidationException(missing);
        }

        return new WorkflowDefinition(tracker, polling, agent, copilot, workspace, hooks, body);
    }

    private static Dictionary<string, object?> ParseYaml(string yaml)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);
        var stack = new Stack<(int indent, Dictionary<string, object?> map)>();
        stack.Push((-1, root));

        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            if (indent % 2 != 0)
            {
                throw new FormatException($"Invalid YAML indentation at line {i + 1}.");
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                throw new FormatException($"Top-level list items are not supported at line {i + 1}.");
            }

            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new FormatException($"Invalid YAML key/value at line {i + 1}.");
            }

            var key = trimmed[..separatorIndex].Trim();
            var valuePart = trimmed[(separatorIndex + 1)..].Trim();

            while (stack.Count > 0 && stack.Peek().indent >= indent)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                throw new FormatException($"Malformed YAML structure at line {i + 1}.");
            }

            var current = stack.Peek().map;

            if (string.IsNullOrEmpty(valuePart))
            {
                if (TryReadList(lines, i + 1, indent + 2, out var list, out var consumed))
                {
                    current[key] = list;
                    i += consumed;
                    continue;
                }

                var child = new Dictionary<string, object?>(StringComparer.Ordinal);
                current[key] = child;
                stack.Push((indent, child));
                continue;
            }

            current[key] = ParseScalarOrInline(valuePart);
        }

        return root;
    }

    private static bool TryReadList(
        IReadOnlyList<string> lines,
        int start,
        int expectedIndent,
        out List<object?> values,
        out int consumed)
    {
        values = [];
        consumed = 0;

        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                consumed++;
                continue;
            }

            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            if (indent < expectedIndent)
            {
                break;
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            values.Add(ParseScalarOrInline(trimmed[2..].Trim()));
            consumed++;
        }

        return values.Count > 0;
    }

    private static object? ParseScalarOrInline(string value)
    {
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var inner = value[1..^1].Trim();
            if (string.IsNullOrEmpty(inner))
            {
                return new List<object?>();
            }

            return inner
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseScalarOrInline)
                .ToList();
        }

        if (value.StartsWith('{') && value.EndsWith('}'))
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            var inner = value[1..^1].Trim();

            if (string.IsNullOrEmpty(inner))
            {
                return result;
            }

            var entries = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var separatorIndex = entry.IndexOf(':');
                if (separatorIndex < 0)
                {
                    throw new FormatException("Invalid inline map in YAML.");
                }

                var key = entry[..separatorIndex].Trim();
                var mapValue = entry[(separatorIndex + 1)..].Trim();
                result[key] = ParseScalarOrInline(mapValue);
            }

            return result;
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static TrackerConfig BuildTracker(Dictionary<string, object?> tracker)
    {
        return new TrackerConfig(
            GetString(tracker, "kind"),
            GetString(tracker, "api_key"),
            GetString(tracker, "owner"),
            GetInt(tracker, "project_number"),
            GetStrings(tracker, "active_states"),
            GetStrings(tracker, "terminal_states"),
            GetString(tracker, "priority_field"),
            GetString(tracker, "handoff_status", "In Review"),
            GetString(tracker, "endpoint", "https://api.github.com/graphql"));
    }

    private static PollingConfig BuildPolling(Dictionary<string, object?> polling)
        => new(GetInt(polling, "interval_ms", 10_000));

    private static AgentConfig BuildAgent(Dictionary<string, object?> agent)
        => new(
            GetInt(agent, "max_concurrent_agents", 1),
            GetIntMap(agent, "max_concurrent_agents_by_state"),
            GetInt(agent, "max_retry_backoff_ms", 300_000),
            GetInt(agent, "max_turns", 50));

    private static CopilotConfig BuildCopilot(Dictionary<string, object?> copilot)
        => new(
            GetString(copilot, "model", "gpt-5"),
            GetInt(copilot, "turn_timeout_ms", 60_000),
            GetInt(copilot, "stall_timeout_ms", 120_000));

    private static WorkspaceConfig BuildWorkspace(Dictionary<string, object?> workspace)
        => new(GetString(workspace, "root", ".harmonia/workspaces"));

    private static HooksConfig BuildHooks(Dictionary<string, object?> hooks)
        => new(
            GetStringNullable(hooks, "after_create"),
            GetStringNullable(hooks, "before_run"),
            GetStringNullable(hooks, "after_run"),
            GetStringNullable(hooks, "before_remove"),
            GetInt(hooks, "timeout_ms", 300_000));

    private static Dictionary<string, object?> GetSection(Dictionary<string, object?> root, string section)
    {
        if (root.TryGetValue(section, out var value) && value is Dictionary<string, object?> map)
        {
            return map;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static string GetString(Dictionary<string, object?> map, string key, string defaultValue = "")
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? defaultValue;
    }

    private static string? GetStringNullable(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(converted) ? null : converted;
    }

    private static int GetInt(Dictionary<string, object?> map, string key, int defaultValue = 0)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => checked((int)longValue),
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static IReadOnlyCollection<string> GetStrings(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        if (value is IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>()
                .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }

        var single = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : [single];
    }

    private static IReadOnlyDictionary<string, int> GetIntMap(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is not Dictionary<string, object?> dictionary)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        return dictionary.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                int intValue => intValue,
                string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0
            },
            StringComparer.Ordinal);
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Changed -= OnWorkflowChanged;
        _watcher.Created -= OnWorkflowChanged;
        _watcher.Renamed -= OnWorkflowChanged;
        _watcher.Dispose();
        _watcher = null;
    }
}
