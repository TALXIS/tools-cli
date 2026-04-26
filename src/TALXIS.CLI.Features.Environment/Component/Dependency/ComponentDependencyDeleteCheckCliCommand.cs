using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

[CliReadOnly]
[CliCommand(
    Name = "delete-check",
    Description = "Check whether a component can be safely deleted by listing blocking dependencies."
)]
public class ComponentDependencyDeleteCheckCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentDependencyDeleteCheckCliCommand));

    [CliArgument(Name = "component-id", Description = "Component GUID.")]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type (name or code, e.g. 'Entity' or '1').", Required = true)]
    public string Type { get; set; } = null!;

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
            Logger.LogError("Unknown component type '{Type}'.", Type);
            return ExitValidationError;
        }

        var service = TxcServices.Get<ISolutionDependencyService>();
        var deps = await service.CheckDeleteAsync(Profile, id, typeCode, CancellationToken.None).ConfigureAwait(false);

        if (deps.Count == 0)
        {
            OutputFormatter.WriteData(
                new { status = "safe", componentId = ComponentId, componentType = Type, blockingDependencies = 0 },
                _ =>
                {
#pragma warning disable TXC003
                    OutputWriter.WriteLine($"No blocking dependencies — component {ComponentId} ({Type}) can be safely deleted.");
#pragma warning restore TXC003
                });
            return ExitSuccess;
        }

        OutputFormatter.WriteData(
            new { status = "blocked", componentId = ComponentId, componentType = Type, blockingDependencies = deps.Count, dependencies = deps },
            _ => DependencyOutputHelper.PrintDependencyTable(deps, "Dependent", "Required",
                $"Deleting component {ComponentId} ({Type}) is blocked by {deps.Count} dependency(ies)."));
        return ExitError;
    }
}
