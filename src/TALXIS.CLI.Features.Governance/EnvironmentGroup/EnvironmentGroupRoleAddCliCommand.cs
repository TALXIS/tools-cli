using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Grants a user, Entra group, or service principal an RBAC role on an
/// environment group. The role applies to every environment currently in
/// the group and every environment added to it later.
/// Usage: <c>txc governance environment-group role add &lt;environment-group&gt; --principal-type &lt;type&gt; --principal &lt;value&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Grant a user, Entra group, or service principal an RBAC role (e.g. Owner, Contributor, Reader) on an environment group. The role applies to every environment currently in the group and any added later."
)]
public class EnvironmentGroupRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupRoleAddCliCommand));

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    [CliOption(Name = "--principal-type", Description = "Type of principal being granted the role.", Required = true)]
    public PowerPlatformPrincipalType PrincipalType { get; set; }

    [CliOption(
        Name = "--principal",
        Description = "The principal to grant the role to. User/service-principal: object id, user principal name, app id, or display name. Group: Entra object id (GUID) only.",
        Required = true)]
    public string Principal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Role name or role id (e.g. Owner, Contributor, Reader).", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddAsync();

    private async Task<int> ExecuteAddAsync()
    {
        try
        {
            var assignment = await EnvironmentGroupRoleCommandSupport
                .AddRoleAsync(Profile, EnvironmentGroup, PrincipalType, Principal, Role, CancellationToken.None)
                .ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                environmentGroup = EnvironmentGroup,
                roleAssignmentId = assignment.RoleAssignmentId,
                principalType = assignment.PrincipalType.ToString(),
                principalObjectId = assignment.PrincipalObjectId,
                roleDefinitionId = assignment.RoleDefinitionId,
            };

            EnvironmentGroupRoleOutput.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' granted to {PrincipalType} '{Principal}' on environment group '{EnvironmentGroup}'.");
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
