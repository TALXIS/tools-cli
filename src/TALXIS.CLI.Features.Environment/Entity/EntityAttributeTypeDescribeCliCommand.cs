using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Returns the parameter schema for a specific attribute type (always JSON, for MCP consumption).
/// Usage: <c>txc environment entity attribute type describe &lt;type&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "describe",
    Description = "Describe the parameter schema for an attribute type."
)]
#pragma warning disable TXC003
public class EntityAttributeTypeDescribeCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeTypeDescribeCliCommand));

    [CliArgument(Name = "type", Description = "The attribute type name to describe (e.g. string, lookup, datetime).")]
    public string Type { get; set; } = null!;

    protected override Task<int> ExecuteAsync()
    {
        var info = AttributeTypeRegistry.Get(Type);
        if (info is null)
        {
            OutputWriter.WriteLine($"Unknown attribute type '{Type}'. Use 'txc environment entity attribute type list' to see available types.");
            return Task.FromResult(ExitError);
        }

        var payload = new Dictionary<string, object?>
        {
            ["type"] = info.Name,
            ["description"] = info.Description,
            ["sdkType"] = info.SdkType,
            ["parameters"] = BuildParametersMap(info.Parameters),
            ["sharedParameters"] = AttributeTypeRegistry.SharedParameterNames,
        };

        OutputWriter.WriteLine(JsonSerializer.Serialize(payload, TxcOutputJsonOptions.Default));
        return Task.FromResult(ExitSuccess);
    }

    /// <summary>Builds a dictionary keyed by parameter name with its metadata.</summary>
    private static Dictionary<string, object?> BuildParametersMap(IReadOnlyList<AttributeParameterInfo> parameters)
    {
        var map = new Dictionary<string, object?>();
        foreach (var p in parameters)
        {
            var entry = new Dictionary<string, object?>
            {
                ["type"] = p.Type,
                ["description"] = p.Description,
                ["sdkProperty"] = p.SdkProperty,
            };

            if (p.Default is not null)
                entry["default"] = p.Default;
            if (p.EnumValues is not null)
                entry["values"] = p.EnumValues;
            if (p.Min.HasValue)
                entry["min"] = p.Min.Value;
            if (p.Max.HasValue)
                entry["max"] = p.Max.Value;

            map[p.Name] = entry;
        }
        return map;
    }

}
