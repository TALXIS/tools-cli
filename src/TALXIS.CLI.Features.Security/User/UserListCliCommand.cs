using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Lists Entra users tenant-wide, or Dataverse environment users when an environment scope is resolved.
/// Usage: <c>txc security user list [--filter &lt;upn-or-name&gt;] [--enabled|--disabled|--all] [--environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Entra users when no environment is resolved. When --environment is provided or the active connection already targets an environment, list Dataverse environment users instead; in that mode --enabled, --disabled, and --all control the Dataverse user state filter."
)]
public class UserListCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only users whose user principal name or display name starts with this value. When an environment scope is resolved, this option is ignored because Dataverse user listing currently exposes state filters instead.", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--enabled", Description = "When an environment scope is resolved, show enabled Dataverse users only. This is the default when no Dataverse state flag is supplied.", Required = false)]
    public bool Enabled { get; set; }

    [CliOption(Name = "--disabled", Description = "When an environment scope is resolved, show disabled Dataverse users only.", Required = false)]
    public bool Disabled { get; set; }

    [CliOption(Name = "--all", Description = "When an environment scope is resolved, show both enabled and disabled Dataverse users.", Required = false)]
    public bool All { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        if (scope.HasEnvironment)
        {
            if (!SecurityPrincipalCommandSupport.TryResolveStateFilter(Enabled, Disabled, All, Logger, out var filter))
                return ExitValidationError;

            var service = TxcServices.Get<IDataverseUserService>();
            var rows = await service.ListAsync(Profile, filter, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, UserCommandSupport.PrintEnvironmentUsersTable);
            return ExitSuccess;
        }

        if (Enabled || Disabled || All)
        {
            Logger.LogError("--enabled, --disabled, and --all require --environment or an active environment connection.");
            return ExitValidationError;
        }

        var users = await UserCommandSupport.ListUsersAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(users, UserCommandSupport.PrintUserList);
        return ExitSuccess;
    }
}
