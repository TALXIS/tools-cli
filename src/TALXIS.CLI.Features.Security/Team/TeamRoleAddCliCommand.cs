using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Team;

[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a Dataverse security role to a team in the resolved environment. Not supported for access teams because Dataverse uses them only for record sharing. Pass --environment explicitly or use a profile already connected to an environment."
)]
public class TeamRoleAddCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamRoleAddCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Exact role name or role GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security team role add", CancellationToken.None).ConfigureAwait(false);
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var existingRoles = await service.ListRolesAsync(Profile, Team, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            if (existingRoles.Any(r => SecurityPrincipalCommandSupport.IsRoleMatch(r, Role)))
            {
                OutputFormatter.WriteResult("unchanged", $"Role '{Role}' is already assigned to team '{Team}'.");
                return ExitSuccess;
            }

            await service.AddRoleAsync(Profile, Team, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
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
