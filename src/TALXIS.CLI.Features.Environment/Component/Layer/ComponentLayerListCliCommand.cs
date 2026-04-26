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

    [CliArgument(Name = "component-id", Description = "Component GUID (objectId from solution component).")]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type name (e.g. Entity, Attribute, Workflow).", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--show-json", Description = "Show full component JSON per layer.", Required = false)]
    public bool ShowJson { get; set; }

    [CliOption(Name = "--show-changes", Description = "Show delta/diff JSON per layer.", Required = false)]
    public bool ShowChanges { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionLayerQueryService>();
        var layers = await service.ListLayersAsync(Profile, ComponentId, Type, CancellationToken.None).ConfigureAwait(false);

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
