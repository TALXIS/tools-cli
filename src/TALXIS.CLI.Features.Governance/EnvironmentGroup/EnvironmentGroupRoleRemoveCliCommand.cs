using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Revokes an RBAC role assignment from an environment group.
/// Usage: <c>txc governance environment-group role remove &lt;environment-group&gt; --principal-type &lt;type&gt; --principal &lt;value&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently revokes the role assignment from the environment group.")]
[CliCommand(
    Name = "remove",
    Description = "Remove (revoke) a role assignment from an environment group."
)]
public class EnvironmentGroupRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    [CliOption(Name = "--principal-type", Description = "Type of principal the role is being revoked from.", Required = true)]
    public PowerPlatformPrincipalType PrincipalType { get; set; }

    [CliOption(
        Name = "--principal",
        Description = "The principal to revoke the role from. User/service-principal: object id, user principal name, app id, or display name. Group: Entra object id (GUID) only.",
        Required = true)]
    public string Principal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Role name or role id (e.g. Owner, Contributor, Reader).", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveAsync();

    private async Task<int> ExecuteRemoveAsync()
    {
        try
        {
            await EnvironmentGroupRoleCommandSupport
                .RemoveRoleAsync(Profile, EnvironmentGroup, PrincipalType, Principal, Role, CancellationToken.None)
                .ConfigureAwait(false);

            var payload = new
            {
                status = "role-removed",
                environmentGroup = EnvironmentGroup,
                principalType = PrincipalType.ToString(),
                principal = Principal,
                role = Role,
            };

            EnvironmentGroupRoleOutput.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' revoked from {PrincipalType} '{Principal}' on environment group '{EnvironmentGroup}'.");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (EnvironmentGroupRoleOutput.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
