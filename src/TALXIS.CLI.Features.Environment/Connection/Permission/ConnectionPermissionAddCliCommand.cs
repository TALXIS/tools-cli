using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// <c>txc environment connection permission add</c> — shares a connection.
/// </summary>
/// <remarks>
/// The check that matters for ALM: binding a connection reference to a
/// connection, or turning on a flow that uses it, fails with
/// <c>ConnectionAuthorizationFailed</c> unless the acting identity owns the
/// connection or it has been shared with them.
/// </remarks>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Share a connection with a user, service principal or group in the LIVE connected environment. Requires an active profile. Idempotent: a principal that already has the role is left unchanged."
)]
public class ConnectionPermissionAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionPermissionAddCliCommand));

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

    [CliOption(Name = "--role", Description = "Role to grant: can-view (can use the connection, the default), can-view-with-share or can-edit.", Required = false)]
    public string? Role { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!ConnectionCommandSupport.TryBuildPrincipal(User, ServicePrincipal, Group, out var principal, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        if (!ConnectionCommandSupport.TryParseRole(Role, out var role))
        {
            Logger.LogError("Unknown role '{Role}'. Use can-view, can-view-with-share or can-edit.", Role);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectionPermissionService>();
        var outcome = await service.AddPermissionAsync(
            Profile, Connector, ConnectionId, principal!, role, CancellationToken.None).ConfigureAwait(false);

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
                var who = outcome.Permission?.DisplayName ?? principal!.Identifier;
                var granted = outcome.Permission is { } given ? ConnectionRoleNames.ToCli(given.Role) : ConnectionRoleNames.ToCli(role);
                OutputWriter.WriteLine(outcome.Status == ConnectionOutcomeStatus.Unchanged
                    ? $"{who} already has '{granted}' on connection {outcome.ConnectionId}."
                    : $"Connection {outcome.ConnectionId}: {who} {outcome.Status.ToKebabCase()} as '{granted}'.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
