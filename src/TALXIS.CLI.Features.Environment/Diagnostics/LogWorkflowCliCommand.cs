using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Reads classic (background) workflow run history. These runs are
/// <c>asyncoperation</c> rows whose operation type is Workflow (10).
/// </summary>
/// <example>
///   txc environment log workflow --since 7d
///   txc env log workflow --errors-only --entity account
/// </example>
[CliReadOnly]
[CliCommand(
    Name = "workflow",
    Description = "Read classic background workflow run history from the LIVE environment. Requires an active profile."
)]
public class LogWorkflowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LogWorkflowCliCommand));

    [CliOption(Name = "--since", Description = "Relative time window, e.g. 30m, 24h, 7d, 2w.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--entity", Description = "Filter by the workflow's regarding-object entity logical name (best-effort, client-side).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--errors-only", Description = "Only failed or cancelled workflow runs.", Required = false)]
    public bool ErrorsOnly { get; set; }

    [CliOption(Name = "--correlation-id", Description = "Restrict to a single operation's correlation id (GUID).", Required = false)]
    public string? CorrelationId { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of runs to return.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!EnvLogFilterBuilder.TryBuild(Since, Entity, plugin: null, ErrorsOnly, CorrelationId, Top, out var filter, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IEnvironmentLogService>();
        var rows = await service.GetWorkflowRunsAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, r => LogAsyncJobsCliCommand.PrintTable(r, "No workflow runs found."));
        return ExitSuccess;
    }
}
