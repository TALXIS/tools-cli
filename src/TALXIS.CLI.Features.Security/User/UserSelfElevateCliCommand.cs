using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Grants the current authenticated caller Dataverse admin access in the selected environment.
/// Usage: <c>txc security user self-elevate [--environment &lt;id&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "self-elevate",
    Description = "Grant the current authenticated caller the environment admin role in a Dataverse environment. This command requires --environment or an active environment connection because there is no tenant-wide equivalent."
)]
public class UserSelfElevateCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserSelfElevateCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security user self-elevate", CancellationToken.None).ConfigureAwait(false);
        await TxcServices.Get<TALXIS.CLI.Core.Platforms.PowerPlatform.IEnvironmentUserProvisioningService>()
            .SelfElevateAsync(scope.EnvironmentContext!.Connection, scope.EnvironmentContext.Credential, scope.EnvironmentId!.Value, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = "elevated", environmentId = scope.EnvironmentId, caller = "current" },
            _ => OutputWriter.WriteLine("Environment admin role applied to the current authenticated caller."));

        return ExitSuccess;
    }
}
