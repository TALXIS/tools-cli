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
    Name = "list",
    Description = "List Dataverse teams in the resolved environment. Pass --environment explicitly or use a profile already connected to an environment."
)]
public class TeamListCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security team list", CancellationToken.None).ConfigureAwait(false);
        var service = TxcServices.Get<IDataverseTeamService>();
        var rows = await service.ListAsync(Profile, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
        OutputFormatter.WriteList(rows, TeamCommandSupport.WriteTeamList);
        return ExitSuccess;
    }
}
