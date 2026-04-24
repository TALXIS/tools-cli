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
public class EnvDataRecordCreateCliCommand : ProfiledCliCommand
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

    /// <summary>
    /// Validates that exactly one of <c>--data</c> / <c>--file</c> is provided
    /// and parses the JSON into a <see cref="JsonElement"/>.
    /// </summary>
    private bool TryParseAttributes(out JsonElement attributes)
    {
        attributes = default;

        bool hasData = !string.IsNullOrWhiteSpace(Data);
        bool hasFile = !string.IsNullOrWhiteSpace(File);

        if (hasData == hasFile)
        {
            _logger.LogError("Provide exactly one of --data or --file.");
            return false;
        }

        string json;
        if (hasFile)
        {
            if (!System.IO.File.Exists(File))
            {
                _logger.LogError("File not found: {Path}", File);
                return false;
            }
            json = System.IO.File.ReadAllText(File!);
        }
        else
        {
            json = Data!;
        }

        try
        {
            attributes = JsonDocument.Parse(json).RootElement;
            if (attributes.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Expected a JSON object, got {Kind}.", attributes.ValueKind);
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError("Invalid JSON: {Error}", ex.Message);
            return false;
        }
    }
}
