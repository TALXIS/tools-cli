using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Show what depends on this component."
)]
public class ComponentDependencyListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentDependencyListCliCommand));

    [CliArgument(Name = "component-id", Description = "Component GUID.")]
    public string ComponentId { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Component type (name or code, e.g. 'Entity' or '1').", Required = true)]
    public string Type { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        if (!TryParseComponentArgs(out var id, out var typeCode))
            return ExitValidationError;

        var service = TxcServices.Get<ISolutionDependencyService>();
        var deps = await service.GetDependentsAsync(Profile, id, typeCode, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(deps, rows => DependencyOutputHelper.PrintDependencyTable(rows, "Dependent", "Required"));
        return ExitSuccess;
    }

    private bool TryParseComponentArgs(out Guid id, out int typeCode)
    {
        id = Guid.Empty;
        typeCode = 0;

        if (!Guid.TryParse(ComponentId, out id))
        {
            Logger.LogError("Invalid component ID '{ComponentId}'. Must be a valid GUID.", ComponentId);
            return false;
        }

        var resolver = new ComponentTypeResolver();
        if (!resolver.TryResolveCode(Type, out typeCode))
        {
            Logger.LogError("Unknown component type '{Type}'. Use a type code (e.g. 1) or name (e.g. Entity).", Type);
            return false;
        }

        return true;
    }
}
