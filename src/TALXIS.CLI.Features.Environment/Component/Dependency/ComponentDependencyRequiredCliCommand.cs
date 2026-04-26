using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

[CliReadOnly]
[CliCommand(
    Name = "required",
    Description = "Show what this component depends on."
)]
public class ComponentDependencyRequiredCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentDependencyRequiredCliCommand));

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
        var deps = await service.GetRequiredAsync(Profile, id, typeCode, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(deps, rows => DependencyOutputHelper.PrintDependencyTable(rows, "Required", "Dependent"));
        return ExitSuccess;
    }
}
