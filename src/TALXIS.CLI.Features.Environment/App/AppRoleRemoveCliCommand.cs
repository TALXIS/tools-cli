using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Removes a security role from a Dataverse application user.
/// Usage: <c>txc environment app role remove --app &lt;client-id-or-guid&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently removes the security role assignment from the Dataverse application user.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a security role from a Dataverse application user."
)]
public class AppRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--app", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string App { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Role name or GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();
            await service.RemoveRoleAsync(Profile, App, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-removed",
                app = App,
                role = Role,
            };

            AppCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from application user '{App}'.");
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
