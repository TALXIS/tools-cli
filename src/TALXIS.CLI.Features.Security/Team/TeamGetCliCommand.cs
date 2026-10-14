using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Team;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get details for a Dataverse team by exact name or GUID in the resolved environment. Pass --environment explicitly or use a profile already connected to an environment."
)]
public class TeamGetCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamGetCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteGetAsync();

    private async Task<int> ExecuteGetAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security team get", CancellationToken.None).ConfigureAwait(false);
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var team = await service.GetAsync(Profile, Team, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
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
