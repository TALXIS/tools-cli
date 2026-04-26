using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliDestructive("Permanently removes unmanaged customizations from the component. This cannot be undone.")]
[CliCommand(
    Name = "remove-customization",
    Description = "Remove the unmanaged active layer from a component, reverting to the highest managed layer."
)]
public class ComponentLayerRemoveCustomizationCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentLayerRemoveCustomizationCliCommand));

    [CliOption(Name = "--id", Description = "Component GUID (MetadataId / objectId). Required unless --entity is given.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--type", Description = "Component type name. Auto-detected when using --entity.", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name. Resolves MetadataId automatically.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (requires --entity).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var resolved = await ComponentIdResolver.TryResolveAsync(Id, Type, Entity, Attribute, Profile, Logger, CancellationToken.None).ConfigureAwait(false);
        if (resolved is null)
            return ExitValidationError;
        var (componentId, typeName) = resolved.Value;
            return ExitValidationError;

        if (!Guid.TryParse(componentId, out var guid))
        {
            Logger.LogError("Invalid component ID '{ComponentId}'. Must be a valid GUID.", componentId);
            return ExitValidationError;
        }

        // Pre-check: verify an active layer exists
        var layerService = TxcServices.Get<ISolutionLayerQueryService>();
        var layers = await layerService.ListLayersAsync(Profile, componentId, typeName, CancellationToken.None).ConfigureAwait(false);

        var activeLayer = layers.FirstOrDefault(l => l.SolutionName == "Active");
        if (activeLayer is null)
        {
            Logger.LogWarning("No active (unmanaged) layer found for component {ComponentId}. Nothing to remove.", componentId);
            return ExitSuccess;
        }

        if (layers.Count == 1)
        {
            Logger.LogError("The active layer is the only layer for this component. Cannot remove it — delete the component instead.");
            return ExitError;
        }

        // Execute removal
        var mutationService = TxcServices.Get<ISolutionLayerMutationService>();
        await mutationService.RemoveCustomizationAsync(Profile, guid, typeName, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = "removed", componentId, componentType = typeName },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Removed active customization layer for {typeName} {componentId}. Component reverted to managed layer behavior.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
