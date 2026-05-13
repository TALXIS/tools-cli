using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Add an existing component to an unmanaged solution."
)]
public class SolutionComponentAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionComponentAddCliCommand));

    [CliArgument(Name = "solution", Description = "Target solution unique name.")]
    public string SolutionName { get; set; } = null!;

    [CliOption(Name = "--component-id", Description = "Component GUID.", Required = true)]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type (name or code, e.g. 'Entity' or '1').", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--add-required", Description = "Also add required dependent components.", Required = false)]
    public bool AddRequired { get; set; }

    [CliOption(Name = "--exclude-subcomponents", Description = "Do not include subcomponents.", Required = false)]
    public bool ExcludeSubcomponents { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!Guid.TryParse(ComponentId, out var id))
        {
            Logger.LogError("Invalid component ID '{ComponentId}'. Must be a valid GUID.", ComponentId);
            return ExitValidationError;
        }

        var def = ComponentDefinitionRegistry.GetByName(Type);
        if (def is null && int.TryParse(Type, out var parsedCode))
            def = ComponentDefinitionRegistry.GetByType((ComponentType)parsedCode);
        if (def is null)
        {
            var known = string.Join(", ", ComponentDefinitionRegistry.GetAll().Select(d => d.Name).Take(15));
            Logger.LogError("Unknown component type '{Type}'. Available types: {Known}. Or use an integer code.", Type, known);
            return ExitValidationError;
        }
        var typeCode = (int)def.TypeCode;

        // Pre-check: reject managed solutions (can't add components to managed)
        var detailService = TxcServices.Get<ISolutionDetailService>();
        var (solution, _) = await detailService.ShowAsync(Profile, SolutionName, CancellationToken.None).ConfigureAwait(false);
        if (solution.Managed)
        {
            Logger.LogError("Cannot add components to managed solution '{SolutionName}'.", SolutionName);
            return ExitError;
        }

        var options = new ComponentAddOptions(SolutionName, id, typeCode, AddRequired, ExcludeSubcomponents);
        var service = TxcServices.Get<ISolutionComponentMutationService>();
        await service.AddAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        var typeName = def.Name;
        OutputFormatter.WriteData(
            new { status = "added", solution = SolutionName, componentId = ComponentId, componentType = typeName },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Added {typeName} {ComponentId} to solution '{SolutionName}'.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
