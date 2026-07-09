using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.Group;

/// <summary>
/// Lists Entra groups available for tenant-wide role assignment operations.
/// Usage: <c>txc tenant group list [--filter &lt;name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Entra groups by display name in the connected tenant."
)]
public class GroupListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(GroupListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only groups whose display name starts with this value.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var groups = await GroupCommandSupport.ListGroupsAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(groups, GroupCommandSupport.PrintGroupList);
        return ExitSuccess;
    }
}
