using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Lists RBAC role assignments (Owner/Contributor/Reader/RBAC Administrator,
/// or any other tenant role) held directly on an environment group.
/// Usage: <c>txc governance environment-group role list &lt;environment-group&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List role assignments (users, groups, and service principals) granted directly on an environment group. These assignments apply to every environment in the group."
)]
public class EnvironmentGroupRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentGroupRoleListCliCommand));

    [CliArgument(Description = "Environment group id (GUID) or display name.")]
    public string EnvironmentGroup { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync() => ExecuteListAsync();

    private async Task<int> ExecuteListAsync()
    {
        try
        {
            var assignments = await EnvironmentGroupRoleCommandSupport
                .ListRolesAsync(Profile, EnvironmentGroup, CancellationToken.None)
                .ConfigureAwait(false);

            OutputFormatter.WriteList(assignments, EnvironmentGroupRoleOutput.PrintList);
            return ExitSuccess;
        }
        catch (Exception ex) when (EnvironmentGroupRoleOutput.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
