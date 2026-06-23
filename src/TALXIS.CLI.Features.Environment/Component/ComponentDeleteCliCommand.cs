using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.Component.Dependency;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component;

[CliDestructive("Permanently deletes the component object from the environment. This cannot be undone.")]
[CliCommand(
    Name = "delete",
    Description = "Permanently delete a component object from the LIVE environment (e.g. a leftover security role, plugin step, or assembly blocking an import). Refuses if other components still depend on it — run 'environment component dependency delete-check' first to preview. Different from 'solution component remove', which only unlinks a component from a solution without deleting it."
)]
public class ComponentDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentDeleteCliCommand));

    [CliOption(Name = "--id", Description = "Component GUID (objectId / MetadataId). Required unless --entity is given.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--type", Description = "Component type (name or code, e.g. 'Role' or '20'). Auto-detected when using --entity.", Required = false)]
    public string? Type { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name. Resolves MetadataId automatically.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (requires --entity).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--yes", Description = "Skip the interactive confirmation for this destructive delete.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var ct = CancellationToken.None;

        var resolved = await ComponentIdResolver.TryResolveAsync(Id, Type, Entity, Attribute, Profile, Logger, ct).ConfigureAwait(false);
        if (resolved is null)
            return ExitValidationError;
        var (componentId, typeName) = resolved.Value;

        if (!Guid.TryParse(componentId, out var id))
        {
            Logger.LogError("Invalid component ID '{ComponentId}'. Must be a valid GUID.", componentId);
            
            return ExitValidationError;
        }

        var def = ComponentDefinitionRegistry.GetByName(typeName);
        if (def is null && int.TryParse(typeName, out var parsedCode))
            def = ComponentDefinitionRegistry.GetByType((ComponentType)parsedCode);
        if (def is null)
        {
            var known = string.Join(", ", ComponentDefinitionRegistry.GetAll().Select(d => d.Name).Take(15));
            Logger.LogError("Unknown component type '{Type}'. Available types: {Known}. Or use an integer code.", typeName, known);

            return ExitValidationError;
        }
        var typeCode = (int)def.TypeCode;

        if (!SolutionComponentEntityMap.IsSupported(typeCode))
        {
            Logger.LogError(
                "Deleting '{TypeName}' from the environment is not supported by this command. Supported component entities: {Supported}. " +
                "Tables and columns are schema objects — use workspace/schema tooling instead.",
                typeName, SolutionComponentEntityMap.SupportedSummary);

            return ExitValidationError;
        }

        // Safety: refuse to delete while other components still depend on this one.
        var dependencyService = TxcServices.Get<ISolutionDependencyService>();
        var blockers = await dependencyService.CheckDeleteAsync(Profile, id, typeCode, ct).ConfigureAwait(false);
        if (blockers.Count > 0)
        {
            OutputFormatter.WriteData(
                new { status = "blocked", componentId, componentType = typeName, blockingDependencies = blockers.Count, dependencies = blockers },
                _ => DependencyOutputHelper.PrintDependencyTable(blockers, "Dependent", "Required",
                    $"Cannot delete {typeName} {componentId}: {blockers.Count} component(s) still depend on it. Remove or repoint them first."));
            return ExitError;
        }

        var service = TxcServices.Get<ISolutionComponentMutationService>();
        await service.DeleteFromEnvironmentAsync(Profile, typeCode, id, ct).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = "deleted", componentId, componentType = typeName },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Deleted {typeName} {componentId} from the environment.");
#pragma warning restore TXC003
            });
        return ExitSuccess;
    }
}
