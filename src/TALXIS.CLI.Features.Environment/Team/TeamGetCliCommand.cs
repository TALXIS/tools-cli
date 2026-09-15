using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Gets details for a Dataverse team.
/// Usage: <c>txc environment team get --team &lt;name-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get details for a Dataverse team by exact name or GUID."
)]
public class TeamGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamGetCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteGetAsync();

    private async Task<int> ExecuteGetAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var team = await service.GetAsync(Profile, Team, CancellationToken.None).ConfigureAwait(false);
            if (team is null)
            {
                Logger.LogError("Dataverse team '{Team}' was not found.", Team);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(team, TeamCommandSupport.WriteTeamDetail);
            return ExitSuccess;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
    }
}
