using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Adds a direct member to an owner or access Dataverse team.
/// Usage: <c>txc environment team member add --team &lt;name-or-guid&gt; --user &lt;upn-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Add a direct member to an owner or access team. AAD-backed team membership is managed in Entra ID and is rejected by this command."
)]
public class TeamMemberAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamMemberAddCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--user", Description = "User principal name or user GUID.", Required = true)]
    public string User { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddMemberAsync();

    private async Task<int> ExecuteAddMemberAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            await service.AddMemberAsync(Profile, Team, User, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteResult("succeeded", $"Added user '{User}' to team '{Team}'.");
            return ExitSuccess;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("was not found.", StringComparison.Ordinal) || ex.Message.Contains("managed in Entra ID", StringComparison.Ordinal))
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
    }
}
