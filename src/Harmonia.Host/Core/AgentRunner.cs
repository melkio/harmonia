using System.Text.RegularExpressions;

namespace Harmonia.Host.Core;

public sealed partial class AgentRunner(
    IAgentEngine agentEngine,
    IProjectTrackerClient trackerClient,
    IHookRunner hookRunner) : IAgentRunner
{
    [GeneratedRegex("{{\\s*issue\\.(identifier|title|body)\\s*}}", RegexOptions.CultureInvariant)]
    private static partial Regex IssueTokenRegex();

    public async Task<AgentRunResult> RunAsync(
        WorkflowDefinition workflow,
        ProjectItem item,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await hookRunner.RunAsync(workflow.Hooks.BeforeRun, workspacePath, workflow.Hooks.TimeoutMs, cancellationToken);

            var prompt = RenderPrompt(workflow.PromptTemplate, item);
            var context = new AgentExecutionContext(
                workspacePath,
                prompt,
                workflow.Copilot.Model,
                workflow.Agent.MaxTurns,
                workflow.Copilot.TurnTimeoutMs,
                workflow.Copilot.StallTimeoutMs,
                (query, variables, ct) => trackerClient.ExecuteGraphQlAsync(workflow.Tracker, query, variables, ct));

            var resultType = await agentEngine.RunAsync(context, cancellationToken);
            return new AgentRunResult(resultType);
        }
        catch (OperationCanceledException)
        {
            return new AgentRunResult(AgentRunResultType.Stalled, "Agent run cancelled or stalled.");
        }
        catch (Exception ex)
        {
            return new AgentRunResult(AgentRunResultType.Error, ex.Message);
        }
        finally
        {
            await hookRunner.RunAsync(workflow.Hooks.AfterRun, workspacePath, workflow.Hooks.TimeoutMs, cancellationToken);
        }
    }

    internal static string RenderPrompt(string template, ProjectItem item)
    {
        return IssueTokenRegex().Replace(template, match => match.Groups[1].Value switch
        {
            "identifier" => item.IssueIdentifier,
            "title" => item.Title,
            "body" => item.Body,
            _ => match.Value
        });
    }
}
