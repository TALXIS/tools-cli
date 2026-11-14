using System.ComponentModel;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Data;

[CliDestructive("Permanently deletes every record listed in the CMT data package from the target Dataverse environment.")]
[CliLongRunning]
[CliWorkflow("data-operations")]
[CliCommand(
    Name = "cleanup",
    Description = "Deletes every record contained in a CMT data package from the LIVE Dataverse environment referenced by the active profile. Intended for tearing down test data inserted by a previous 'data package import'."
)]
public class DataPackageCleanupCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DataPackageCleanupCliCommand));

    [CliArgument(Description = "Path to the CMT data package (.zip file or folder containing data.xml and data_schema.xml)")]
    public required string Data { get; set; }

    [CliOption(Name = "--connection-count", Description = "How many parallel connections to open. Entities are sharded across connections — higher values speed up cleanup of many small entities at the cost of more concurrent throttle pressure.", Required = false)]
    [DefaultValue(1)]
    public int ConnectionCount { get; set; } = 1;

    [CliOption(Name = "--batch-size", Description = "How many DeleteRequest messages to send per ExecuteMultiple batch. Lower is safer, higher is faster.", Required = false)]
    [DefaultValue(200)]
    public int BatchSize { get; set; } = 200;

    [CliOption(Name = "--dry-run", Description = "Parse the package and report what would be deleted without issuing any DeleteRequest.", Required = false)]
    [DefaultValue(false)]
    public bool DryRun { get; set; }

    [CliOption(Name = "--missing-action", Description = "What to do when a record can't be deleted by its GUID: by-natural-key (look it up via primary-name + updateCompare fields), skip (count as not-found), or fail (abort the run).", Required = false)]
    [DefaultValue("by-natural-key")]
    public string MissingAction { get; set; } = "by-natural-key";

    [CliOption(Name = "--continue-on-error", Description = "Keep going after the first per-record failure. Default true — set to false to abort on the first error.", Required = false)]
    [DefaultValue(true)]
    public bool ContinueOnError { get; set; } = true;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Data))
        {
            Logger.LogError("A path to a CMT data package (.zip or folder) must be provided.");
            return ExitValidationError;
        }

        if (!File.Exists(Data) && !Directory.Exists(Data))
        {
            Logger.LogError("Data package not found: {DataPath}", Data);
            return ExitValidationError;
        }

        if (BatchSize <= 0)
        {
            Logger.LogError("--batch-size must be greater than zero (got {BatchSize}).", BatchSize);
            return ExitValidationError;
        }

        if (ConnectionCount <= 0)
        {
            Logger.LogError("--connection-count must be greater than zero (got {ConnectionCount}).", ConnectionCount);
            return ExitValidationError;
        }

        if (!TryParseMissingAction(MissingAction, out var missingAction))
        {
            Logger.LogError("--missing-action must be one of: by-natural-key, skip, fail (got '{Value}').", MissingAction);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IDataPackageService>();
        var options = new DataPackageCleanupOptions(BatchSize, ConnectionCount, DryRun, missingAction, ContinueOnError);

        var result = await service.CleanupAsync(Profile, Data, options, Verbose, CancellationToken.None).ConfigureAwait(false);

        if (result.InteractiveAuthRequired)
        {
            Logger.LogError("Interactive authentication is required. Run 'txc config auth login' for profile '{Profile}' and retry.", Profile ?? "(default)");
            OutputFormatter.WriteResult("failed", "Interactive authentication required.", exitCode: ExitError);
            return ExitError;
        }

        if (result.ErrorMessage is not null)
        {
            Logger.LogError("{ErrorMessage}", result.ErrorMessage);
            OutputFormatter.WriteResult("failed", result.ErrorMessage, exitCode: ExitError);
            return ExitError;
        }

        EmitReport(result);

        if (!result.Succeeded)
        {
            Logger.LogError(
                "Data package cleanup completed with errors. Deleted: {DeletedByGuid} by id, {DeletedByNaturalKey} by natural key. Not found: {NotFound}. Errors: {Errors}.",
                result.TotalDeletedByGuid, result.TotalDeletedByNaturalKey, result.TotalNotFound, result.TotalErrors);
            OutputFormatter.WriteResult("failed", $"{result.TotalErrors} record(s) failed to delete.", exitCode: ExitError);
            return ExitError;
        }

        var summary = DryRun
            ? $"Dry run: would delete {result.TotalDeletedByGuid} record(s) and disassociate {result.M2mDisassociations} M:N pair(s)."
            : $"Deleted {result.TotalDeletedByGuid + result.TotalDeletedByNaturalKey} record(s) ({result.TotalDeletedByNaturalKey} via natural-key fallback). {result.TotalNotFound} not found. Disassociated {result.M2mDisassociations} M:N pair(s).";
        OutputFormatter.WriteResult("succeeded", summary);
        return ExitSuccess;
    }

    private void EmitReport(DataPackageCleanupResult result)
    {
        if (!OutputContext.IsJson)
        {
            foreach (var entity in result.EntityResults)
            {
                if (entity.Total == 0)
                    continue;
                Logger.LogInformation(
                    "{Entity}: {Total} record(s) — deleted {DeletedByGuid} by id, {DeletedByNaturalKey} by natural key, {NotFound} not found, {Errors} error(s).",
                    entity.EntityLogicalName, entity.Total, entity.DeletedByGuid, entity.DeletedByNaturalKey, entity.NotFound, entity.Errors);
                foreach (var message in entity.ErrorMessages)
                    Logger.LogWarning("  {ErrorMessage}", message);
            }
            return;
        }

        var payload = new
        {
            entities = result.EntityResults.Select(e => new
            {
                entity = e.EntityLogicalName,
                total = e.Total,
                deletedByGuid = e.DeletedByGuid,
                deletedByNaturalKey = e.DeletedByNaturalKey,
                notFound = e.NotFound,
                errors = e.Errors,
                errorMessages = e.ErrorMessages,
            }).ToArray(),
            totals = new
            {
                deletedByGuid = result.TotalDeletedByGuid,
                deletedByNaturalKey = result.TotalDeletedByNaturalKey,
                notFound = result.TotalNotFound,
                errors = result.TotalErrors,
                m2mDisassociations = result.M2mDisassociations,
            },
            dryRun = DryRun,
        };
        OutputFormatter.WriteData(payload);
    }

    /// <summary>
    /// Pure helper kept for test coverage: parses the textual value passed to
    /// <c>--missing-action</c> into the corresponding enum.
    /// </summary>
    public static bool TryParseMissingAction(string? value, out DataPackageCleanupMissingAction action)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "by-natural-key":
            case "natural-key":
            case "naturalkey":
                action = DataPackageCleanupMissingAction.ByNaturalKey;
                return true;
            case "skip":
                action = DataPackageCleanupMissingAction.Skip;
                return true;
            case "fail":
                action = DataPackageCleanupMissingAction.Fail;
                return true;
            default:
                action = DataPackageCleanupMissingAction.ByNaturalKey;
                return false;
        }
    }
}
