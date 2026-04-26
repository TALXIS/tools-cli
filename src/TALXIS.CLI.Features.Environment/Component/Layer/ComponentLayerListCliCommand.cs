using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Show solution layer stack for a component."
)]
public class ComponentLayerListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentLayerListCliCommand));

    [CliOption(Name = "--id", Description = "Component GUID (MetadataId / objectId). Required unless --entity is given.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--type", Description = "Component type name (e.g. Entity, Attribute). Auto-detected when using --entity.", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name. Resolves MetadataId automatically.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (requires --entity). Resolves attribute MetadataId.", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--show-json", Description = "Show full component JSON per layer.", Required = false)]
    public bool ShowJson { get; set; }

    [CliOption(Name = "--show-changes", Description = "Show delta/diff JSON per layer.", Required = false)]
    public bool ShowChanges { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var resolved = await ComponentIdResolver.TryResolveAsync(Id, Type, Entity, Attribute, Profile, Logger, CancellationToken.None).ConfigureAwait(false);
        if (resolved is null)
            return ExitValidationError;
        var (componentId, typeName) = resolved.Value;
            return ExitValidationError;

        var service = TxcServices.Get<ISolutionLayerQueryService>();
        var layers = await service.ListLayersAsync(Profile, componentId, typeName, CancellationToken.None).ConfigureAwait(false);

        bool showJson = ShowJson;
        bool showChanges = ShowChanges;
        OutputFormatter.WriteList(layers, rows => PrintLayersTable(rows, showJson, showChanges));
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintLayersTable(IReadOnlyList<ComponentLayerRow> rows, bool showJson, bool showChanges)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No layers found.");
            return;
        }

        int slnWidth = Math.Clamp(rows.Max(r => r.SolutionName.Length), 12, 40);
        int pubWidth = Math.Clamp(rows.Max(r => (r.PublisherName ?? "").Length), 9, 30);
        int nameWidth = Math.Clamp(rows.Max(r => (r.Name ?? "").Length), 4, 30);

        string header =
            $"{"Order",5} | {"Solution".PadRight(slnWidth)} | {"Publisher".PadRight(pubWidth)} | {"Name".PadRight(nameWidth)} | OverwriteTime";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            OutputWriter.WriteLine(
                $"{r.Order,5} | " +
                $"{r.SolutionName.PadRight(slnWidth)} | " +
                $"{(r.PublisherName ?? "").PadRight(pubWidth)} | " +
                $"{(r.Name ?? "").PadRight(nameWidth)} | " +
                $"{r.OverwriteTime:u}");

            if (showJson && !string.IsNullOrWhiteSpace(r.ComponentJson))
            {
                OutputWriter.WriteLine("  --- Component JSON ---");
                OutputWriter.WriteLine(PrettyPrintJson(r.ComponentJson));
            }
            if (showChanges && !string.IsNullOrWhiteSpace(r.Changes))
            {
                OutputWriter.WriteLine("  --- Changes ---");
                OutputWriter.WriteLine(PrettyPrintJson(r.Changes));
            }
        }
    }
#pragma warning restore TXC003

    private static string PrettyPrintJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, TxcOutputJsonOptions.Default);
        }
        catch
        {
            return json;
        }
    }

}
