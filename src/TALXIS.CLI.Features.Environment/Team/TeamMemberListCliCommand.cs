using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Lists direct members of a Dataverse team.
/// Usage: <c>txc environment team member list --team &lt;name-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List direct members of a Dataverse team. For AAD-backed teams, the list reflects current team membership but add/remove is managed in Entra ID."
)]
public class TeamMemberListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamMemberListCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListMembersAsync();

    private async Task<int> ExecuteListMembersAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var rows = await service.ListMembersAsync(Profile, Team, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, TeamCommandSupport.WriteMemberList);
            return ExitSuccess;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("was not found.", StringComparison.Ordinal))
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
    }
}
