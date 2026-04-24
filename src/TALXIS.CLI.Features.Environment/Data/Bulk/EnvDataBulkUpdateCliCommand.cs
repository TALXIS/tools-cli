using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Updates multiple records of the same entity type in a single request
/// using the Dataverse <c>UpdateMultiple</c> SDK message.
/// </summary>
[CliCommand(
    Name = "update",
    Description = "Update multiple records in a single UpdateMultiple request."
)]
public class EnvDataBulkUpdateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataBulkUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--file", Description = "Path to a JSON file containing an array of records.", Required = false)]
    public string? File { get; set; }

    [CliOption(Name = "--data", Description = "Inline JSON array of records.", Required = false)]
    public string? Data { get; set; }

    [CliOption(Name = "--json", Description = "Emit the result as JSON.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        if (!BulkInputHelper.TryParseRecords(File, Data, _logger, out var records))
            return 1;

        BulkOperationResult result;
        try
        {
            var service = TxcServices.Get<IDataverseBulkService>();
            result = await service.UpdateMultipleAsync(Profile, Entity, records, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException or FileNotFoundException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "bulk update failed");
            return 1;
        }

        BulkOutputHelper.WriteResult("UpdateMultiple", result, Json);
        return 0;
    }
}
