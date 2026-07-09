using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Lists Dataverse teams in the current environment.
/// Usage: <c>txc environment team list</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Dataverse teams in the current environment."
)]
public class TeamListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();
        var rows = await service.ListAsync(Profile, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, TeamCommandSupport.WriteTeamList);
        return ExitSuccess;
    }
}
