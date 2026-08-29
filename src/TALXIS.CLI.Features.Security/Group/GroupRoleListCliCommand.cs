using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Group;

/// <summary>
/// Lists tenant roles assigned to an Entra group.
/// Usage: <c>txc security group role list --group &lt;object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List tenant roles assigned to an Entra group in the connected tenant."
)]
public class GroupRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(GroupRoleListCliCommand));

    [CliOption(Name = "--group", Description = "Entra group object id (GUID). Find it via the Entra admin center or 'az ad group show --group <name> --query id -o tsv'.", Required = true)]
    public string Group { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListRolesAsync();

    private async Task<int> ExecuteListRolesAsync()
    {
        try
        {
            var assignments = await GroupCommandSupport.ListRolesAsync(Profile, Group, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(assignments, SecurityPrincipalCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
