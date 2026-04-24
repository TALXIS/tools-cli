using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Updates a single record identified by entity logical name and record ID.
/// </summary>
[CliCommand(
    Name = "update",
    Description = "Update a single record by ID from JSON attributes."
)]
public class EnvDataRecordUpdateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record to update.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--data", Description = "Inline JSON object with attributes to update.", Required = false)]
    public string? Data { get; set; }

    [CliOption(Name = "--file", Description = "Path to a JSON file containing attributes to update.", Required = false)]
    public string? File { get; set; }

    public async Task<int> RunAsync()
    {
        if (!TryParseAttributes(out var attributes))
            return 1;

        try
        {
            var service = TxcServices.Get<IDataverseRecordService>();
            await service.UpdateAsync(Profile, Entity, RecordId, attributes, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException or FileNotFoundException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record update failed");
            return 1;
        }

        OutputWriter.WriteLine("Record updated successfully.");
        return 0;
    }

    private bool TryParseAttributes(out JsonElement attributes)
        => RecordInputHelper.TryParseAttributes(Data, File, _logger, out attributes);
}
