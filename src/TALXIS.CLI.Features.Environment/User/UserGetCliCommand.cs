using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Gets a single Dataverse environment user by UPN or GUID.
/// Usage: <c>txc environment user get --user &lt;upn-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a single environment user by user principal name or system user GUID."
)]
#pragma warning disable TXC003
public class UserGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserGetCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or system user GUID.", Required = true)]
    public string User { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseUserService>();
        var user = await UserCliCommandSupport.ResolveUserAsync(
            service,
            Profile,
            User,
            Logger,
            CancellationToken.None).ConfigureAwait(false);
        if (user is null)
            return ExitValidationError;

        OutputFormatter.WriteData(user, UserCliCommandSupport.PrintUserDetail);
        return ExitSuccess;
    }
}
