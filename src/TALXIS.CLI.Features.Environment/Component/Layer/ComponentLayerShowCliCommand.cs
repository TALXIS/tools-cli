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

    [CliOption(Name = "--id", Description = "Component GUID (MetadataId / objectId). Required unless --entity is given.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--type", Description = "Component type name. Auto-detected when using --entity.", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name. Resolves MetadataId automatically.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (requires --entity).", Required = false)]
    public string? Attribute { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var resolved = await ComponentIdResolver.TryResolveAsync(Id, Type, Entity, Attribute, Profile, Logger, CancellationToken.None).ConfigureAwait(false);
        if (resolved is null)
            return ExitValidationError;
        var (componentId, typeName) = resolved.Value;

        var service = TxcServices.Get<ISolutionLayerQueryService>();
        var json = await service.GetActiveLayerJsonAsync(Profile, componentId, typeName, CancellationToken.None).ConfigureAwait(false);

        if (json is null)
        {
            Logger.LogWarning("No active layer found for component {ComponentId} of type {Type}.", componentId, typeName);
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
