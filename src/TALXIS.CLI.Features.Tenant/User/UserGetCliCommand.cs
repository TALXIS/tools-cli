using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.User;

/// <summary>
/// Gets one Entra user by user principal name or object id.
/// Usage: <c>txc tenant user get --user &lt;upn-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Entra user by user principal name or object id in the connected tenant."
)]
public class UserGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserGetCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object id.", Required = true)]
    public string User { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var user = await UserCommandSupport.GetUserAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteData(user, UserCommandSupport.PrintUserDetail);
        return ExitSuccess;
    }
}
