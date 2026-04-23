using System.ComponentModel;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Abstractions;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Environment.Solution;

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

        SolutionInfo source;
        try
        {
            source = SolutionImporter.ReadSolutionInfo(solutionPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            _logger.LogError(ex, "Unable to read solution metadata from {Path}", solutionPath);
            return 1;
        }

        _logger.LogInformation("Source solution: {UniqueName} {Version} ({Managed})",
            source.UniqueName, source.Version, source.Managed ? "managed" : "unmanaged");

        DataverseConnection conn;
        try
        {
            conn = await DataverseCommandBridge.ConnectAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }

        using (conn)
        {
            var client = conn.Client;
            try
            {
                var importer = new SolutionImporter(client, _logger);
                var existing = await importer.GetExistingSolutionAsync(source.UniqueName).ConfigureAwait(false);
                var plannedPath = SolutionImporter.SelectImportPath(source, existing, StageAndUpgrade);
                bool smartDiffExpected = SolutionImporter.SmartDiffExpected(plannedPath, ForceOverwrite);

                _logger.LogInformation("Planned import path: {Path}", FormatPath(plannedPath));
                _logger.LogInformation("SmartDiff expected: {SmartDiff}", smartDiffExpected ? "yes" : "no");

                EmitWarnings(plannedPath, ForceOverwrite);

                var options = new SolutionImportOptions(
                    StageAndUpgrade: StageAndUpgrade,
                    ForceOverwrite: ForceOverwrite,
                    PublishWorkflows: PublishWorkflows,
                    SkipDependencyCheck: SkipDependencyCheck,
                    SkipLowerVersion: SkipLowerVersion,
                    Async: !Wait);

                var result = await importer.ImportAsync(solutionPath, options).ConfigureAwait(false);

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Solution import failed");
                return 1;
            }
        }
    }

    private void EmitWarnings(SolutionImportPath plannedPath, bool forceOverwrite)
    {
        if (forceOverwrite && plannedPath == SolutionImportPath.Upgrade)
        {
            _logger.LogWarning("--force-overwrite disables SmartDiff; expect a full re-import.");
        }

        if (plannedPath == SolutionImportPath.Update)
        {
            _logger.LogWarning("Plain update does not delete components removed from the source solution.");
        }
    }

    private static string FormatPath(SolutionImportPath path) => path switch
    {
        SolutionImportPath.Install => "install",
        SolutionImportPath.Update => "update",
        SolutionImportPath.Upgrade => "single-step upgrade",
        _ => path.ToString()
    };
}
