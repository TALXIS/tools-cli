using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Group;

/// <summary>
/// Removes a tenant role from an Entra group.
/// Usage: <c>txc security group role remove --group &lt;object-id&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently removes the tenant role assignment from the group.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a tenant role from an Entra group in the connected tenant."
)]
public class GroupRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(GroupRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--group", Description = "Entra group object id (GUID). Find it via the Entra admin center or 'az ad group show --group <name> --query id -o tsv'.", Required = true)]
    public string Group { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or role id.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        try
        {
            await GroupCommandSupport.RemoveRoleAsync(Profile, Group, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-removed",
                group = Group,
                role = Role,
            };

            SecurityPrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from group '{Group}'.");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
