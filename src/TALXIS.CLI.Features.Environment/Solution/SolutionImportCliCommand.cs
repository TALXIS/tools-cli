using System.ComponentModel;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliIdempotent]
[CliCommand(
    Name = "import",
    Description = "Import a solution .zip into the target environment."
)]
public class SolutionImportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionImportCliCommand));

    [CliArgument(Name = "solution-zip", Description = "Path to the solution .zip to import.")]
    public required string SolutionZip { get; set; }

    [CliOption(Name = "--stage-and-upgrade", Description = "Use single-step upgrade when applicable.", Required = false)]
    [DefaultValue(true)]
    public bool StageAndUpgrade { get; set; } = true;

    [CliOption(Name = "--force-overwrite", Description = "Overwrite unmanaged customizations (disables SmartDiff).", Required = false)]
    public bool ForceOverwrite { get; set; }

    [CliOption(Name = "--publish-workflows", Description = "Activate workflows after import.", Required = false)]
    public bool PublishWorkflows { get; set; }

    [CliOption(Name = "--skip-dependency-check", Description = "Skip product-update dependency checks.", Required = false)]
    public bool SkipDependencyCheck { get; set; }

    [CliOption(Name = "--skip-lower-version", Description = "Skip import when source version is not higher than target.", Required = false)]
    public bool SkipLowerVersion { get; set; }

    [CliOption(Name = "--wait", Description = "Wait for completion. By default solution imports return after queueing.", Required = false)]
    public bool Wait { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(SolutionZip))
        {
            Logger.LogError("'solution-zip' argument is required.");
            return ExitValidationError;
        }

        string solutionPath = Path.GetFullPath(SolutionZip);
        if (!File.Exists(solutionPath))
        {
            Logger.LogError("Solution file not found: {Path}", solutionPath);
            return ExitValidationError;
        }

        var options = new SolutionImportOptions(
            StageAndUpgrade: StageAndUpgrade,
            ForceOverwrite: ForceOverwrite,
            PublishWorkflows: PublishWorkflows,
            SkipDependencyCheck: SkipDependencyCheck,
            SkipLowerVersion: SkipLowerVersion,
            Async: !Wait);

        var service = TxcServices.Get<ISolutionImportService>();
        var result = await service.ImportAsync(Profile, solutionPath, options, CancellationToken.None).ConfigureAwait(false);

        if (OutputContext.IsJson)
        {
            var payload = new
            {
                path = FormatPath(result.Path),
                uniqueName = result.Source.UniqueName,
                sourceVersion = result.Source.Version.ToString(),
                sourceManaged = result.Source.Managed,
                existingVersion = result.ExistingTarget?.Version.ToString(),
                existingManaged = result.ExistingTarget?.Managed,
                importJobId = result.ImportJobId,
                asyncOperationId = result.AsyncOperationId,
                startedAtUtc = result.StartedAtUtc.ToString("O"),
                completedAtUtc = result.CompletedAtUtc?.ToString("O"),
                smartDiffExpected = result.SmartDiffExpected,
                status = result.Status,
            };
#pragma warning disable TXC003 // TODO: Refactor to use OutputFormatter
            OutputWriter.WriteLine(JsonSerializer.Serialize(payload, TxcOutputJsonOptions.Default));
#pragma warning restore TXC003
        }

        Logger.LogInformation("Import path: {Path}", FormatPath(result.Path));
        Logger.LogInformation("Status: {Status}", result.Status);
        Logger.LogInformation("ImportJobId: {ImportJobId}", result.ImportJobId);
        if (result.AsyncOperationId is { } asyncId)
        {
            Logger.LogInformation("AsyncOperationId: {AsyncOperationId}", asyncId);
        }
        Logger.LogInformation("Started (UTC): {Start}", result.StartedAtUtc.ToString("O"));
        if (result.CompletedAtUtc is { } completed)
        {
            Logger.LogInformation("Completed (UTC): {End}", completed.ToString("O"));
        }

        return ExitSuccess;
    }

    private static string FormatPath(SolutionImportPath path) => path switch
    {
        SolutionImportPath.Install => "install",
        SolutionImportPath.Update => "update",
        SolutionImportPath.Upgrade => "single-step upgrade",
        _ => path.ToString()
    };
}
