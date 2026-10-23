using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Lists Dataverse environment users provisioned from Entra ID.
/// Usage: <c>txc environment user list [--enabled|--disabled|--all]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List environment users. Defaults to enabled users when no state flag is supplied."
)]
#pragma warning disable TXC003
public class UserListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserListCliCommand));

    [CliOption(Name = "--enabled", Description = "Show enabled users only. This is the default when no state flag is supplied.", Required = false)]
    public bool Enabled { get; set; }

    [CliOption(Name = "--disabled", Description = "Show disabled users only.", Required = false)]
    public bool Disabled { get; set; }

    [CliOption(Name = "--all", Description = "Show both enabled and disabled users.", Required = false)]
    public bool All { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!UserCliCommandSupport.TryResolveStateFilter(Enabled, Disabled, All, Logger, out var filter))
            return ExitValidationError;

        var service = TxcServices.Get<IDataverseUserService>();
        var rows = await service.ListAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, UserCliCommandSupport.PrintUsersTable);
        return ExitSuccess;
    }
}
