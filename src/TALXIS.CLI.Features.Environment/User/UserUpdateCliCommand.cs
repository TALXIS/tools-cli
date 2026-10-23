using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Enables or disables an existing Dataverse environment user.
/// Usage: <c>txc environment user update --user &lt;upn-or-guid&gt; [--enable|--disable]</c>
/// </summary>
[CliDestructive("Disabling an environment user removes their active access to the environment until they are enabled again.")]
[CliCommand(
    Name = "update",
    Description = "Enable a user or disable a user to remove their active environment access. Resolves the target by user principal name or system user GUID. Specify exactly one of --enable or --disable."
)]
#pragma warning disable TXC003
public class UserUpdateCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserUpdateCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--user", Description = "User principal name or system user GUID.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--enable", Description = "Enable the user.", Required = false)]
    public bool Enable { get; set; }

    [CliOption(Name = "--disable", Description = "Disable the user.", Required = false)]
    public bool Disable { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (Enable == Disable)
        {
            Logger.LogError("Specify exactly one of --enable or --disable.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<IDataverseUserService>();
        var user = await UserCliCommandSupport.ResolveUserAsync(
            service,
            Profile,
            User,
            Logger,
            CancellationToken.None).ConfigureAwait(false);
        if (user is null)
            return ExitValidationError;

        bool targetEnabled = Enable;
        if (user.IsDisabled == !targetEnabled)
        {
            var currentState = targetEnabled ? "enabled" : "disabled";
            OutputFormatter.WriteData(
                new
                {
                    status = "unchanged",
                    userId = user.Id,
                    user = UserCliCommandSupport.FormatUserLabel(user),
                    enabled = targetEnabled,
                },
                _ => OutputWriter.WriteLine($"User '{UserCliCommandSupport.FormatUserLabel(user)}' is already {currentState}."));
            return ExitSuccess;
        }

        await service.UpdateEnabledStateAsync(Profile, User, targetEnabled, CancellationToken.None).ConfigureAwait(false);

        var newState = targetEnabled ? "enabled" : "disabled";
        OutputFormatter.WriteData(
            new
            {
                status = newState,
                userId = user.Id,
                user = UserCliCommandSupport.FormatUserLabel(user),
                enabled = targetEnabled,
            },
            _ => OutputWriter.WriteLine($"User '{UserCliCommandSupport.FormatUserLabel(user)}' {newState}."));

        return ExitSuccess;
    }
}
