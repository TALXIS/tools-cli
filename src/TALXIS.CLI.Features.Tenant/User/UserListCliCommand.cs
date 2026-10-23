using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.User;

/// <summary>
/// Lists Entra users available for tenant-wide role assignment operations.
/// Usage: <c>txc tenant user list [--filter &lt;upn-or-name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Entra users by user principal name or display name in the connected tenant."
)]
public class UserListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only users whose user principal name or display name starts with this value.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var users = await UserCommandSupport.ListUsersAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(users, UserCommandSupport.PrintUserList);
        return ExitSuccess;
    }
}
