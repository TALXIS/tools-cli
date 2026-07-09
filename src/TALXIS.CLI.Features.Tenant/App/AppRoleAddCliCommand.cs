using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.App;

/// <summary>
/// Assigns a tenant-wide role to an Entra application.
/// Usage: <c>txc tenant app role add --app &lt;client-id-or-object-id&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a tenant-wide role to an Entra application."
)]
public class AppRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppRoleAddCliCommand));

    [CliOption(Name = "--app", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string App { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or GUID. Use 'admin-application' to allow this app to call txc environment admin commands non-interactively.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        try
        {
            await TenantAppCommandSupport.AddAssignmentAsync(Profile, App, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                app = App,
                role = Role,
            };

            TenantAppCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to app '{App}'.");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (TenantAppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
