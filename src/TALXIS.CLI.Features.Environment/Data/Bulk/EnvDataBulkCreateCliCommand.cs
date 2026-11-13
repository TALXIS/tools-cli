using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Creates multiple records of the same entity type in batched requests
/// using the Dataverse <c>CreateMultiple</c> SDK message.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Creates multiple Dataverse records in batched requests on the LIVE connected environment. Requires an active profile. Accepts JSON array via --data or --file. Column types are auto-detected: option sets accept plain integers (e.g. 375970000), money fields accept decimals, lookups accept {Id,LogicalName} objects or a bare GUID string (single-target lookups)."
)]
public class EnvDataBulkCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataBulkCreateCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Path to a JSON file containing an array of records.", Required = false)]
    public string? File { get; set; }

    [CliOption(Name = "--data", Description = "Inline JSON array of records.", Required = false)]
    public string? Data { get; set; }

    [CliOption(Name = "--batch-size", Description = "Records sent per request (1-1000). Keep it small for tables with slow synchronous plugins so each request finishes inside the client timeout.", Required = false)]
    public int BatchSize { get; set; } = BulkInputHelper.DefaultBatchSize;

    protected override async Task<int> ExecuteAsync()
    {
        if (!BulkInputHelper.TryParseRecords(File, Data, Logger, out var records))
            return ExitValidationError;
        if (!BulkInputHelper.IsValidBatchSize(BatchSize, Logger)) return ExitValidationError;

        var service = TxcServices.Get<IDataverseBulkService>();
        var result = await service.CreateMultipleAsync(Profile, Entity, records, BatchSize, CancellationToken.None).ConfigureAwait(false);

        BulkOutputHelper.WriteResult("CreateMultiple", result);
        if (result.Error is null) return ExitSuccess;

        Logger.LogError("{Error}", result.Error);
        return ExitError;
    }
}
