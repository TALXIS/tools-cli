namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// URL builders for the Power Automate maker portal (<c>make.powerautomate.com</c>).
/// Covers flow editor, flow details, run history, and specific run inspection.
/// </summary>
public static class PowerAutomateUrls
{
    private const string Base = "https://make.powerautomate.com";

    /// <summary>Flow base path — editor view (default landing page for a flow).</summary>
    public static Uri FlowEditor(Guid environmentId, Guid flowId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"{Base}/environments/{environmentId}/solutions/{solutionId}/flows/{flowId}")
            : new($"{Base}/environments/{environmentId}/flows/{flowId}");

    /// <summary>Flow details page — shows connection references, owners, and metadata.</summary>
    public static Uri FlowDetails(Guid environmentId, Guid flowId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"{Base}/environments/{environmentId}/solutions/{solutionId}/flows/{flowId}/details")
            : new($"{Base}/environments/{environmentId}/flows/{flowId}/details");

    /// <summary>Flow run history — lists all runs with status.</summary>
    public static Uri FlowRuns(Guid environmentId, Guid flowId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"{Base}/environments/{environmentId}/solutions/{solutionId}/flows/{flowId}/runs")
            : new($"{Base}/environments/{environmentId}/flows/{flowId}/runs");

    /// <summary>Specific flow run — shows the run's step-by-step execution details.</summary>
    public static Uri FlowRun(Guid environmentId, Guid flowId, string runId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"{Base}/environments/{environmentId}/solutions/{solutionId}/flows/{flowId}/runs/{Uri.EscapeDataString(runId)}")
            : new($"{Base}/environments/{environmentId}/flows/{flowId}/runs/{Uri.EscapeDataString(runId)}");
}
