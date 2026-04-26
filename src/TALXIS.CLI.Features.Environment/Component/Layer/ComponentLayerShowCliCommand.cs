using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show the active layer component definition as JSON."
)]
public class ComponentLayerShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentLayerShowCliCommand));

    [CliArgument(Name = "component-id", Description = "Component GUID (objectId from solution component).")]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type name (e.g. Entity, Attribute, Workflow).", Required = true)]
    public string Type { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionLayerQueryService>();
        var json = await service.GetActiveLayerJsonAsync(Profile, ComponentId, Type, CancellationToken.None).ConfigureAwait(false);

        if (json is null)
        {
            Logger.LogWarning("No active layer found for component {ComponentId} of type {Type}.", ComponentId, Type);
            return ExitError;
        }

        OutputFormatter.WriteRaw(json, () =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine(PrettyPrint(json));
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }

    private static string PrettyPrint(string json)
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
