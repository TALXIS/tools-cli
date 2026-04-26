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

    [CliArgument(Name = "component-id", Description = "Component GUID.")]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type (name or code, e.g. 'Entity' or '1').", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!Guid.TryParse(ComponentId, out var id))
        {
            Logger.LogError("Invalid component ID '{ComponentId}'. Must be a valid GUID.", ComponentId);
            return ExitValidationError;
        }

        var resolver = new ComponentTypeResolver();
        if (!resolver.TryResolveCode(Type, out var typeCode))
        {
            var known = string.Join(", ", resolver.GetKnownNames().Take(15));
            Logger.LogError("Unknown component type '{Type}'. Available types: {Known}. Or use an integer code.", Type, known);
            return ExitValidationError;
        }

        // Pre-check: verify an active layer exists
        var layerService = TxcServices.Get<ISolutionLayerQueryService>();
        var layers = await layerService.ListLayersAsync(Profile, ComponentId, resolver.ResolveName(typeCode), CancellationToken.None).ConfigureAwait(false);

        var activeLayer = layers.FirstOrDefault(l => l.SolutionName == "Active");
        if (activeLayer is null)
        {
            Logger.LogWarning("No active (unmanaged) layer found for component {ComponentId}. Nothing to remove.", ComponentId);
            return ExitSuccess;
        }

        if (layers.Count == 1)
        {
            Logger.LogError("The active layer is the only layer for this component. Cannot remove it — delete the component instead.");
            return ExitError;
        }

        // Execute removal
        var typeName = resolver.ResolveName(typeCode);
        var mutationService = TxcServices.Get<ISolutionLayerMutationService>();
        await mutationService.RemoveCustomizationAsync(Profile, id, typeCode, typeName, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteData(
            new { status = "removed", componentId = ComponentId, componentType = typeName },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Removed active customization layer for {typeName} {ComponentId}. Component reverted to managed layer behavior.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
