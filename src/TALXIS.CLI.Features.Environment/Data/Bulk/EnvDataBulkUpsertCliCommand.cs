using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Upserts (creates or updates) multiple records of the same entity type in
/// a single request using the Dataverse <c>UpsertMultiple</c> SDK message.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "upsert",
    Description = "Creates or updates (upserts) multiple Dataverse records by primary/alternate key in a single batch on the LIVE connected environment. Requires an active profile."
)]
public class EnvDataBulkUpsertCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataBulkUpsertCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Path to a JSON file containing an array of records.", Required = false)]
    public string? File { get; set; }

    [CliOption(Name = "--data", Description = "Inline JSON array of records. Special column types: OptionSet as an integer (e.g. 375970000), Lookup as a GUID string or {Id,LogicalName} object, Money as a decimal (e.g. 1500.50), Boolean as true/false, DateTime as an ISO-8601 UTC string (e.g. 2026-05-01T00:00:00Z).", Required = false)]
    public string? Data { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!BulkInputHelper.TryParseRecords(File, Data, Logger, out var records))
            return ExitValidationError;

        var service = TxcServices.Get<IDataverseBulkService>();
        var result = await service.UpsertMultipleAsync(Profile, Entity, records, CancellationToken.None).ConfigureAwait(false);

        BulkOutputHelper.WriteResult("UpsertMultiple", result);
        return ExitSuccess;
    }
}
