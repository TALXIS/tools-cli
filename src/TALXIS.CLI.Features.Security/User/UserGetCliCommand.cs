using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Gets one Entra user tenant-wide, or one Dataverse environment user when an environment scope is resolved.
/// Usage: <c>txc security user get --user &lt;upn-or-object-id&gt; [--environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Entra user by user principal name or object ID when no environment is resolved. When --environment is provided or the active connection already targets an environment, get the Dataverse environment user by user principal name or system user GUID instead."
)]
public class UserGetCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserGetCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object ID. With an environment scope, a Dataverse system user GUID is also accepted.", Required = true)]
    public string User { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        if (scope.HasEnvironment)
        {
            var service = TxcServices.Get<IDataverseUserService>();
            var user = await UserCommandSupport.ResolveEnvironmentUserAsync(
                service,
                Profile,
                User,
                scope.EnvironmentId,
                Logger,
                CancellationToken.None).ConfigureAwait(false);
            if (user is null)
                return ExitValidationError;

            OutputFormatter.WriteData(user, UserCommandSupport.PrintEnvironmentUserDetail);
            return ExitSuccess;
        }

        var tenantUser = await UserCommandSupport.GetUserAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteData(tenantUser, UserCommandSupport.PrintUserDetail);
        return ExitSuccess;
    }
}
