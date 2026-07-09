using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Removes a security role from a Dataverse team.
/// Usage: <c>txc environment team role remove --team &lt;name-or-guid&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Removes the security role assignment from the Dataverse team.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a security role from a Dataverse team. Not supported for access teams (Dataverse restriction: access teams are used only for record sharing, not role-based security) — valid for owner, aad-security-group, and aad-office-group teams. This is destructive."
)]
public class TeamRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamRoleRemoveCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Exact role name or role GUID.", Required = true)]
    public string Role { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            await service.RemoveRoleAsync(Profile, Team, Role, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteResult("succeeded", $"Removed role '{Role}' from team '{Team}'.");
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
