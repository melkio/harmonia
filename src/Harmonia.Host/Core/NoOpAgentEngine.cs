namespace Harmonia.Host.Core;

public sealed class NoOpAgentEngine : IAgentEngine
{
    public Task<AgentRunResultType> RunAsync(AgentExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(AgentRunResultType.Success);
}
