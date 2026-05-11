using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

[CliReadOnly]
[CliCommand(
    Name = "required",
    Description = "Show what this component depends on."
)]
public class ComponentDependencyRequiredCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentDependencyRequiredCliCommand));

    [CliOption(Name = "--id", Description = "Component GUID (MetadataId / objectId). Required unless --entity is given.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--type", Description = "Component type (name or code). Auto-detected when using --entity.", Required = false)]
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
            Logger.LogError("Unknown component type '{Type}'. Available types: {Known}.", typeName, known);
            return ExitValidationError;
        }
        var typeCode = (int)def.TypeCode;

        var service = TxcServices.Get<ISolutionDependencyService>();
        var deps = await service.GetRequiredAsync(Profile, id, typeCode, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(deps, rows => DependencyOutputHelper.PrintDependencyTable(rows, "Required", "Dependent"));
        return ExitSuccess;
    }
}
