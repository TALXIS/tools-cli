using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Enables or disables an existing Dataverse environment user.
/// Usage: <c>txc security user update --user &lt;upn-or-guid&gt; [--enable|--disable] [--environment &lt;id&gt;]</c>
/// </summary>
[CliDestructive("Disabling an environment user removes their active access to the environment until they are enabled again.")]
[CliCommand(
    Name = "update",
    Description = "Enable or disable a Dataverse environment user. This command requires --environment or an active environment connection because there is no tenant-wide user-state mutation equivalent. Specify exactly one of --enable or --disable."
)]
public class UserUpdateCliCommand : SecurityScopedCliCommand, IDestructiveCommand
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

        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security user update", CancellationToken.None).ConfigureAwait(false);
        var service = TxcServices.Get<IDataverseUserService>();
        var user = await UserCommandSupport.ResolveEnvironmentUserAsync(service, Profile, User, scope.EnvironmentId, Logger, CancellationToken.None).ConfigureAwait(false);
        if (user is null)
            return ExitValidationError;

        bool targetEnabled = Enable;
        if (user.IsDisabled == !targetEnabled)
        {
            var currentState = targetEnabled ? "enabled" : "disabled";
            OutputFormatter.WriteData(
                new { status = "unchanged", userId = user.Id, user = UserCommandSupport.FormatUserLabel(user), enabled = targetEnabled, environmentId = scope.EnvironmentId },
                _ => OutputWriter.WriteLine($"User '{UserCommandSupport.FormatUserLabel(user)}' is already {currentState}."));
            return ExitSuccess;
        }

        await service.UpdateEnabledStateAsync(Profile, User, targetEnabled, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);

        var newState = targetEnabled ? "enabled" : "disabled";
        OutputFormatter.WriteData(
            new { status = newState, userId = user.Id, user = UserCommandSupport.FormatUserLabel(user), enabled = targetEnabled, environmentId = scope.EnvironmentId },
            _ => OutputWriter.WriteLine($"User '{UserCommandSupport.FormatUserLabel(user)}' {newState}."));

        return ExitSuccess;
    }
}
