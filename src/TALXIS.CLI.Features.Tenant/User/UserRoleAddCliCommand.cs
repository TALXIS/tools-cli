using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.User;

/// <summary>
/// Assigns a tenant role to an Entra user.
/// Usage: <c>txc tenant user role add --user &lt;upn-or-object-id&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a tenant role to an Entra user in the connected tenant."
)]
public class UserRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleAddCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object id.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or role id.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        try
        {
            await UserCommandSupport.AddRoleAsync(Profile, User, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                user = User,
                role = Role,
            };

            TenantPrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to user '{User}'.");
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
