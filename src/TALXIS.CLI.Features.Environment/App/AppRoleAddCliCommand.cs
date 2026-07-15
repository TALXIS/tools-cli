using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Assigns a security role to a Dataverse application user.
/// Usage: <c>txc environment app role add --app &lt;client-id-or-guid&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a security role to a Dataverse application user."
)]
public class AppRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppRoleAddCliCommand));

    [CliOption(Name = "--app", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string App { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Role name or GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();

            var existingRoles = await service.ListRolesAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);
            if (existingRoles.Any(r => EnvironmentPrincipalCommandSupport.IsRoleMatch(r, Role)))
            {
                AppCommandSupport.WriteMutationResult(
                    new { status = "unchanged", app = App, role = Role },
                    () =>
                    {
#pragma warning disable TXC003
                        OutputWriter.WriteLine($"Role '{Role}' is already assigned to application user '{App}'.");
#pragma warning restore TXC003
                    });
                return ExitSuccess;
            }

            await service.AddRoleAsync(Profile, App, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                app = App,
                role = Role,
            };

            AppCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to application user '{App}'.");
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
