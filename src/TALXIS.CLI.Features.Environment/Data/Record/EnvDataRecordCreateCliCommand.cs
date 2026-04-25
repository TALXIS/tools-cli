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
/// Creates a single record from inline JSON or a JSON file.
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create a single record from JSON attributes."
)]
public class EnvDataRecordCreateCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordCreateCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--data", Description = "Inline JSON object with record attributes.", Required = false)]
    public string? Data { get; set; }

    [CliOption(Name = "--file", Description = "Path to a JSON file containing record attributes.", Required = false)]
    public string? File { get; set; }

    public async Task<int> RunAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "data",
                OperationType = "CREATE",
                TargetType = "record",
                TargetDescription = Entity,
                Details = Data is not null ? "inline JSON" : $"file: {File}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["data"] = Data,
                    ["file"] = File
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE record in '{Entity}'");
            return 0;
        }

        if (!TryParseAttributes(out var attributes))
            return 1;

        Guid createdId;
        try
        {
            var service = TxcServices.Get<IDataverseRecordService>();
            createdId = await service.CreateAsync(Profile, Entity, attributes, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException or FileNotFoundException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record create failed");
            return 1;
        }

        OutputWriter.WriteLine(createdId.ToString());
        return 0;
    }

    private bool TryParseAttributes(out JsonElement attributes)
        => RecordInputHelper.TryParseAttributes(Data, File, _logger, out attributes);
}
