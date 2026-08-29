using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Role;

/// <summary>
/// Lists the tenant role catalog or the Dataverse environment security-role catalog, depending on scope.
/// Usage: <c>txc security role list [--filter &lt;name&gt;] [--environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List tenant roles when no environment is resolved. When --environment is provided or the active connection already targets an environment, switch to that environment's Dataverse security-role catalog instead. The two catalogs are never combined."
)]
public class RoleListCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(RoleListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only tenant roles or Dataverse security roles whose name contains this value.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        if (scope.HasEnvironment)
        {
            var service = TxcServices.Get<IDataverseRoleService>();
            var rows = await service.ListAsync(Profile, NormalizeFilter(Filter), CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, SecurityRoleCommandSupport.PrintEnvironmentRoleList);
            return ExitSuccess;
        }

        var roles = await SecurityRoleCommandSupport.ListTenantRolesAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(roles, RoleOutput.PrintDetailList);
        return ExitSuccess;
    }

    private static string? NormalizeFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
