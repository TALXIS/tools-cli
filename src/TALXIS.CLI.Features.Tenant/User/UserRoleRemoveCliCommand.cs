using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Tenant;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.User;

/// <summary>
/// Removes a tenant role from an Entra user.
/// Usage: <c>txc tenant user role remove --user &lt;upn-or-object-id&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently removes the tenant role assignment from the user.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a tenant role from an Entra user in the connected tenant."
)]
public class UserRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--user", Description = "User principal name or Entra object id.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or role id.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        try
        {
            await UserCommandSupport.RemoveRoleAsync(Profile, User, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-removed",
                user = User,
                role = Role,
            };

            TenantPrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from user '{User}'.");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (TenantPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
