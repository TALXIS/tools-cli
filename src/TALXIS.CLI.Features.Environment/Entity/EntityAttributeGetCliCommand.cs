using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Retrieves detailed metadata for a single attribute (column) on a Dataverse entity.
/// Usage: <c>txc environment entity attribute get --entity &lt;name&gt; --name &lt;name&gt; [-p profile] [--json]</c>
/// </summary>
[CliCommand(
    Name = "get",
    Description = "Get detailed metadata for a single entity attribute (column)."
)]
public class EntityAttributeGetCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeGetCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "Attribute logical name.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--json", Description = "Emit the result as indented JSON instead of key-value text.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        Dictionary<string, object?> detail;
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            detail = await service.GetAttributeDetailAsync(Profile, Entity, Name, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity attribute get failed");
            return 1;
        }

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(detail, JsonOptions));
            return 0;
        }

        PrintDetail(detail);
        return 0;
    }

    /// <summary>Prints attribute detail in vertical key:value format.</summary>
    private static void PrintDetail(Dictionary<string, object?> detail)
    {
        int keyWidth = detail.Keys.Max(k => k.Length);

        foreach (var kvp in detail)
        {
            if (kvp.Key == "Options" && kvp.Value is IEnumerable<object?> options)
            {
                OutputWriter.WriteLine($"{kvp.Key + ":"}");
                foreach (var opt in options)
                {
                    if (opt is Dictionary<string, object?> dict)
                    {
                        var label = dict.GetValueOrDefault("Label")?.ToString() ?? "";
                        var value = dict.GetValueOrDefault("Value")?.ToString() ?? "";
                        OutputWriter.WriteLine($"  {value}: {label}");
                    }
                }
            }
            else if (kvp.Key == "Targets" && kvp.Value is string[] targets)
            {
                OutputWriter.WriteLine($"{"Targets".PadRight(keyWidth)}  {string.Join(", ", targets)}");
            }
            else
            {
                string displayValue = kvp.Value?.ToString() ?? "-";
                OutputWriter.WriteLine($"{kvp.Key.PadRight(keyWidth)}  {displayValue}");
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
