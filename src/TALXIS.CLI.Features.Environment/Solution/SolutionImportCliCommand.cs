using System.ComponentModel;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliCommand(
    Name = "import",
    Description = "Import a solution .zip into the target environment."
)]
public class SolutionImportCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SolutionImportCliCommand));

    [CliArgument(Name = "solution-zip", Description = "Path to the solution .zip to import.", Required = true)]
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

    [CliOption(Name = "--json", Description = "Emit import result as JSON.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(SolutionZip))
        {
            _logger.LogError("'solution-zip' argument is required.");
            return 1;
        }

        string solutionPath = Path.GetFullPath(SolutionZip);
        if (!File.Exists(solutionPath))
        {
            _logger.LogError("Solution file not found: {Path}", solutionPath);
            return 1;
        }

        var options = new SolutionImportOptions(
            StageAndUpgrade: StageAndUpgrade,
            ForceOverwrite: ForceOverwrite,
            PublishWorkflows: PublishWorkflows,
            SkipDependencyCheck: SkipDependencyCheck,
            SkipLowerVersion: SkipLowerVersion,
            Async: !Wait);

        SolutionImportResult result;
        try
        {
            var service = TxcServices.Get<ISolutionImportService>();
            result = await service.ImportAsync(Profile, solutionPath, options, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException or FileNotFoundException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Solution import failed");
            return 1;
        }

        if (Json)
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
            OutputWriter.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        _logger.LogInformation("Import path: {Path}", FormatPath(result.Path));
        _logger.LogInformation("Status: {Status}", result.Status);
        _logger.LogInformation("ImportJobId: {ImportJobId}", result.ImportJobId);
        if (result.AsyncOperationId is { } asyncId)
        {
            _logger.LogInformation("AsyncOperationId: {AsyncOperationId}", asyncId);
        }
        _logger.LogInformation("Started (UTC): {Start}", result.StartedAtUtc.ToString("O"));
        if (result.CompletedAtUtc is { } completed)
        {
            _logger.LogInformation("Completed (UTC): {End}", completed.ToString("O"));
        }

        return 0;
    }

    private static string FormatPath(SolutionImportPath path) => path switch
    {
        SolutionImportPath.Install => "install",
        SolutionImportPath.Update => "update",
        SolutionImportPath.Upgrade => "single-step upgrade",
        _ => path.ToString()
    };
}
