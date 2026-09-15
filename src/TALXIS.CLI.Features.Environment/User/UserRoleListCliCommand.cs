using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Lists security roles assigned to an environment user.
/// Usage: <c>txc environment user role list --user &lt;upn-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List security roles assigned to an environment user."
)]
#pragma warning disable TXC003
public class UserRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleListCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or system user GUID.", Required = true)]
    public string User { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var userService = TxcServices.Get<IDataverseUserService>();
        var user = await UserCliCommandSupport.ResolveUserAsync(
            userService,
            Profile,
            User,
            Logger,
            CancellationToken.None).ConfigureAwait(false);
        if (user is null)
            return ExitValidationError;

        var roles = await userService.ListRolesAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(roles, UserCliCommandSupport.PrintRolesTable);
        return ExitSuccess;
    }
}
