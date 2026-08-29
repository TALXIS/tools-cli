using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.Team;

[CliDestructive("Removing a team security role can immediately reduce what the team can do in the environment.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a Dataverse security role from a team in the resolved environment. Pass --environment explicitly or use a profile already connected to an environment."
)]
public class TeamRoleRemoveCliCommand : SecurityScopedCliCommand, IDestructiveCommand
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
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security team role remove", CancellationToken.None).ConfigureAwait(false);
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            await service.RemoveRoleAsync(Profile, Team, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
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
