using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.Group;

/// <summary>
/// Gets one Entra group by display name or object id.
/// Usage: <c>txc tenant group get --group &lt;name-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Entra group by display name or object id in the connected tenant."
)]
public class GroupGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(GroupGetCliCommand));

    [CliOption(Name = "--group", Description = "Display name or Entra object id.", Required = true)]
    public string Group { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var group = await GroupCommandSupport.GetGroupAsync(Profile, Group, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteData(group, GroupCommandSupport.PrintGroupDetail);
        return ExitSuccess;
    }
}
