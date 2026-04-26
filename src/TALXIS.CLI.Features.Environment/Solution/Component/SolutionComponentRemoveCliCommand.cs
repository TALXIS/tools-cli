using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliDestructive("Removes the component from the solution. The component remains in the environment but is no longer tracked by this solution.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a component from an unmanaged solution (does not delete the component from the environment)."
)]
public class SolutionComponentRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionComponentRemoveCliCommand));

    [CliArgument(Name = "solution", Description = "Solution unique name.")]
    public string SolutionName { get; set; } = null!;

    [CliOption(Name = "--component-id", Description = "Component GUID.", Required = true)]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type (name or code, e.g. 'Entity' or '1').", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
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

        // Pre-check: reject managed solutions (can't remove components from managed)
        var detailService = TxcServices.Get<ISolutionDetailService>();
        var (solution, _) = await detailService.ShowAsync(Profile, SolutionName, CancellationToken.None).ConfigureAwait(false);
        if (solution.Managed)
        {
            Logger.LogError("Cannot remove components from managed solution '{SolutionName}'.", SolutionName);
            return ExitError;
        }

        var options = new ComponentRemoveOptions(SolutionName, id, typeCode);
        var service = TxcServices.Get<ISolutionComponentMutationService>();
        await service.RemoveAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);

        var typeName = resolver.ResolveName(typeCode);
        OutputFormatter.WriteData(
            new { status = "removed", solution = SolutionName, componentId = ComponentId, componentType = typeName },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Removed {typeName} {ComponentId} from solution '{SolutionName}'.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
