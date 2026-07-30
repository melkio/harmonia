namespace Harmonia.Host.Core;

public sealed class OrchestratorHostedService(
    IServiceProvider services,
    ILogger<OrchestratorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workflowPath = Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md");
        if (!File.Exists(workflowPath))
        {
            logger.LogInformation("WORKFLOW.md not found at {Path}. Orchestrator is idle.", workflowPath);
            return;
        }

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<WorkflowLoader>();
        var poller = scope.ServiceProvider.GetRequiredService<IProjectPoller>();
        var dependencyChecker = scope.ServiceProvider.GetRequiredService<IDependencyChecker>();
        var workspaceManager = scope.ServiceProvider.GetRequiredService<IWorkspaceManager>();
        var agentRunner = scope.ServiceProvider.GetRequiredService<IAgentRunner>();

        var workflow = await loader.LoadAsync(stoppingToken);
        loader.StartWatching();
        loader.WorkflowReloaded += (_, updated) => workflow = updated;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var orchestrator = new Orchestrator(
                    workflow,
                    poller,
                    dependencyChecker,
                    workspaceManager,
                    agentRunner);

                await orchestrator.PollOnceAsync(Directory.GetCurrentDirectory(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Orchestrator poll cycle failed.");
            }

            await Task.Delay(workflow.Polling.IntervalMs, stoppingToken);
        }
    }
}
