using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Assigns a security role to a Dataverse team.
/// Usage: <c>txc environment team role add --team &lt;name-or-guid&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a security role to a Dataverse team. Not supported for access teams (Dataverse restriction: access teams are used only for record sharing, not role-based security) — valid for owner, aad-security-group, and aad-office-group teams."
)]
public class TeamRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamRoleAddCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Exact role name or role GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var existingRoles = await service.ListRolesAsync(Profile, Team, CancellationToken.None).ConfigureAwait(false);
            if (existingRoles.Any(r => EnvironmentPrincipalCommandSupport.IsRoleMatch(r, Role)))
            {
                OutputFormatter.WriteResult("unchanged", $"Role '{Role}' is already assigned to team '{Team}'.");
                return ExitSuccess;
            }

            await service.AddRoleAsync(Profile, Team, Role, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteResult("succeeded", $"Added role '{Role}' to team '{Team}'.");
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
