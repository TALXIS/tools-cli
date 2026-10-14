using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

/// <summary>
/// Lists Entra applications tenant-wide, or Dataverse service principals when an environment scope is resolved.
/// Usage: <c>txc security service-principal list [--filter &lt;name&gt;] [--enabled|--disabled|--all] [--environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Entra applications when no environment is resolved. When --environment is provided or the active connection already targets an environment, list Dataverse service principals instead; in that mode --enabled, --disabled, and --all control the Dataverse service-principal state filter."
)]
public class ServicePrincipalListCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only Entra applications whose display name starts with this value. When an environment scope is resolved, this option is ignored because Dataverse service-principal listing currently exposes state filters instead.", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--enabled", Description = "When an environment scope is resolved, list enabled Dataverse service principals only. This is the default when no Dataverse state flag is supplied.", Required = false)]
    public bool Enabled { get; set; }

    [CliOption(Name = "--disabled", Description = "When an environment scope is resolved, list disabled Dataverse service principals only.", Required = false)]
    public bool Disabled { get; set; }

    [CliOption(Name = "--all", Description = "When an environment scope is resolved, list both enabled and disabled Dataverse service principals.", Required = false)]
    public bool All { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        if (scope.HasEnvironment)
        {
            if (!SecurityPrincipalCommandSupport.TryResolveStateFilter(Enabled, Disabled, All, Logger, out var filter))
                return ExitValidationError;

            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var rows = await service.ListAsync(Profile, filter, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, ServicePrincipalCommandSupport.WriteEnvironmentServicePrincipalTable);
            return ExitSuccess;
        }

        if (Enabled || Disabled || All)
        {
            Logger.LogError("--enabled, --disabled, and --all require --environment or an active environment connection.");
            return ExitValidationError;
        }

        var applications = await ServicePrincipalCommandSupport.ListServicePrincipalsAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(applications, ServicePrincipalCommandSupport.WriteServicePrincipalTable);
        return ExitSuccess;
    }
}
