using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Lists security roles assigned to a Dataverse team.
/// Usage: <c>txc environment team role list --team &lt;name-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List security roles assigned to a Dataverse team. Valid for all team types."
)]
public class TeamRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamRoleListCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListRolesAsync();

    private async Task<int> ExecuteListRolesAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var rows = await service.ListRolesAsync(Profile, Team, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, TeamCommandSupport.WriteRoleList);
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
