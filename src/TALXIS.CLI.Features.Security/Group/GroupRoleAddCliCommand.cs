using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Group;

/// <summary>
/// Assigns a tenant role to an Entra group.
/// Usage: <c>txc security group role add --group &lt;object-id&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a tenant role to an Entra group in the connected tenant."
)]
public class GroupRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(GroupRoleAddCliCommand));

    [CliOption(Name = "--group", Description = "Entra group object id (GUID). Find it via the Entra admin center or 'az ad group show --group <name> --query id -o tsv'.", Required = true)]
    public string Group { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or role id.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        try
        {
            await GroupCommandSupport.AddRoleAsync(Profile, Group, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                group = Group,
                role = Role,
            };

            SecurityPrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to group '{Group}'.");
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
