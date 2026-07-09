using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Enables or disables a Dataverse application user.
/// Usage: <c>txc environment app update --app &lt;client-id-or-guid&gt; [--enable|--disable]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Enable or disable a Dataverse application user. Specify exactly one of --enable or --disable."
)]
public class AppUpdateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppUpdateCliCommand));

    [CliOption(Name = "--app", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string App { get; set; } = null!;

    [CliOption(Name = "--enable", Description = "Enable the application user.", Required = false)]
    public bool Enable { get; set; }

    [CliOption(Name = "--disable", Description = "Disable the application user.", Required = false)]
    public bool Disable { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!AppCommandSupport.TryResolveEnabledState(Enable, Disable, Logger, out var enabled))
            return Task.FromResult(ExitValidationError);

        return ExecuteUpdateAsync(enabled);
    }

    private async Task<int> ExecuteUpdateAsync(bool enabled)
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();
            await service.UpdateEnabledStateAsync(Profile, App, enabled, CancellationToken.None).ConfigureAwait(false);

            var updated = await service.GetAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);
            var payload = new
            {
                status = enabled ? "enabled" : "disabled",
                appUser = updated,
                app = App,
            };

            AppCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Application user {(enabled ? "enabled" : "disabled")}.");
                if (updated is not null)
                    AppCommandSupport.WriteAppDetails(updated);
                else
                    OutputWriter.WriteLine($"Identifier: {App}");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (AppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
