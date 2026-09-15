using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Removes a direct member from an owner or access Dataverse team.
/// Usage: <c>txc environment team member remove --team &lt;name-or-guid&gt; --user &lt;upn-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Removes the direct member from the Dataverse team.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a direct member from an owner or access team. AAD-backed team membership is managed in Entra ID and is rejected by this command. This is destructive."
)]
public class TeamMemberRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamMemberRemoveCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--user", Description = "User principal name or user GUID.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override Task<int> ExecuteAsync() => ExecuteRemoveMemberAsync();

    private async Task<int> ExecuteRemoveMemberAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            await service.RemoveMemberAsync(Profile, Team, User, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteResult("succeeded", $"Removed user '{User}' from team '{Team}'.");
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
