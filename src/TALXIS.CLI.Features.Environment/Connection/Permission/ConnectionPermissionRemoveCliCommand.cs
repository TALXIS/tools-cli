using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// <c>txc environment connection permission remove</c> — stops sharing a connection.
/// </summary>
[CliDestructive("Revokes the principal's access to the connection. Flows or connection references the principal bound to it stop working for them.")]
[CliCommand(
    Name = "remove",
    Description = "Stop sharing a connection with a user, service principal or group in the LIVE connected environment. Requires an active profile. The owner cannot be removed."
)]
public class ConnectionPermissionRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionPermissionRemoveCliCommand));

    [CliArgument(Name = "connection-id", Description = "Connection id, exactly as shown by 'environment connection list'.")]
    public string ConnectionId { get; set; } = null!;

    [CliOption(Name = "--connector", Description = "Connector of the connection (logical name, apiId or exact display name).", Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--user", Description = "User principal name or Entra object id.", Required = false)]
    public string? User { get; set; }

    [CliOption(Name = "--service-principal", Description = "Service principal application (client) id or Entra object id.", Required = false)]
    public string? ServicePrincipal { get; set; }

    [CliOption(Name = "--group", Description = "Entra group object id.", Required = false)]
    public string? Group { get; set; }

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!ConnectionCommandSupport.TryBuildPrincipal(User, ServicePrincipal, Group, out var principal, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectionPermissionService>();
        var outcome = await service.RemovePermissionAsync(
            Profile, Connector, ConnectionId, principal!, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                status = outcome.Status.ToKebabCase(),
                connectionId = outcome.ConnectionId,
                apiId = outcome.ApiId,
                permission = outcome.Permission is { } p ? ConnectionPermissionListCliCommand.ToRow(p) : null,
            },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine(outcome.Status == ConnectionOutcomeStatus.Unchanged
                    ? $"{principal!.Identifier} had no access to connection {outcome.ConnectionId}."
                    : $"Removed {outcome.Permission?.DisplayName ?? principal!.Identifier} from connection {outcome.ConnectionId}.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
