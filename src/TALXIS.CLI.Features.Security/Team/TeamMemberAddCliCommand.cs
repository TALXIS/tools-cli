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
    Description = "Add a direct member to an owner or access Dataverse team in the resolved environment. AAD-backed team membership is managed in Entra ID and is rejected by this command. Pass --environment explicitly or use a profile already connected to an environment."
)]
public class TeamMemberAddCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamMemberAddCliCommand));

    [CliOption(Name = "--team", Description = "Exact team name or team GUID.", Required = true)]
    public string Team { get; set; } = null!;

    [CliOption(Name = "--user", Description = "User principal name or user GUID.", Required = true)]
    public string User { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddMemberAsync();

    private async Task<int> ExecuteAddMemberAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security team member add", CancellationToken.None).ConfigureAwait(false);
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            await service.AddMemberAsync(Profile, Team, User, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
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
