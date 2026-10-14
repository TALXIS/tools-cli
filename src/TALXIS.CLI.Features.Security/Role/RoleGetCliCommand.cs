using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Role;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one tenant role when no environment is resolved. When --environment is provided or the active connection already targets an environment, get the Dataverse security role from that environment instead."
)]
public class RoleGetCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(RoleGetCliCommand));

    [CliOption(Name = "--role", Description = "Tenant role name or role ID. With an environment scope, pass a Dataverse security role name or GUID instead.", Required = true)]
    public string Role { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        return scope.HasEnvironment
            ? await ExecuteEnvironmentGetAsync(scope).ConfigureAwait(false)
            : await ExecuteTenantGetAsync().ConfigureAwait(false);
    }

    private async Task<int> ExecuteEnvironmentGetAsync(SecurityScopeContext scope)
    {
        var service = TxcServices.Get<IDataverseRoleService>();

        try
        {
            var row = await service.GetAsync(Profile, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            if (row is null)
            {
                Logger.LogError("Role '{Role}' not found.", Role);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(row, SecurityRoleCommandSupport.PrintEnvironmentRoleDetail);
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task<int> ExecuteTenantGetAsync()
    {
        var role = await SecurityRoleCommandSupport.GetTenantRoleAsync(Profile, Role, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteData(role, RoleOutput.PrintDetail);
        return ExitSuccess;
    }
}
