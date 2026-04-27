using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Updates multiple records of the same entity type in a single request
/// using the Dataverse <c>UpdateMultiple</c> SDK message.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Updates multiple Dataverse records in a single batch request on the LIVE connected environment. Requires an active profile."
)]
public class EnvDataBulkUpdateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataBulkUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Path to a JSON file containing an array of records.", Required = false)]
    public string? File { get; set; }

    [CliOption(Name = "--data", Description = "Inline JSON array of records.", Required = false)]
    public string? Data { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!BulkInputHelper.TryParseRecords(File, Data, Logger, out var records))
            return ExitValidationError;

        var service = TxcServices.Get<IDataverseBulkService>();
        var result = await service.UpdateMultipleAsync(Profile, Entity, records, CancellationToken.None).ConfigureAwait(false);

        BulkOutputHelper.WriteResult("UpdateMultiple", result);
        return ExitSuccess;
    }
}
