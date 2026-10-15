using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// Shares a connection. Binding a connection reference to a connection, or turning
/// on a flow that uses it, fails with <c>ConnectionAuthorizationFailed</c> unless the
/// acting identity owns the connection or it has been shared with them.
/// Usage: <c>txc environment connection permission add &lt;connection-id&gt; [--connector &lt;name&gt;] --user|--service-principal|--group &lt;id&gt; [--role can-view|can-view-with-share|can-edit]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Shares a connection with a user, service principal or group in the LIVE connected environment. Requires an active profile. Idempotent: a principal that already has the role is left unchanged."
)]
public class ConnectionPermissionAddCliCommand : ConnectionPrincipalCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionPermissionAddCliCommand));

    [CliOption(Name = "--role", Description = "Role to grant: can-view (can use the connection), can-view-with-share or can-edit. Defaults to can-view.", Required = false)]
    public string Role { get; set; } = "can-view";

    protected override async Task<int> ExecuteAsync()
    {
        if (!TryBuildPrincipal(out var principal))
            return ExitValidationError;

        if (!ConnectionRoleNames.TryParseCli(Role, out var role))
        {
            Logger.LogError("Unknown role '{Role}'. Use can-view, can-view-with-share or can-edit.", Role);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectionPermissionService>();
        var outcome = await service.AddPermissionAsync(
            Profile, Connector, ConnectionId, principal, role, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                status = outcome.Status.ToKebabCase(),
                connectionId = outcome.ConnectionId,
                apiId = outcome.ApiId,
                permission = outcome.Permission is { } p ? ConnectionPermissionListCliCommand.ToRow(p) : null,
            },
            _ => OutputWriter.WriteLine(Describe(outcome, principal, role)));

        return ExitSuccess;
    }

    private static string Describe(ConnectionPermissionOutcome outcome, ConnectionPrincipalSpec principal, ConnectionPermissionRole requested)
    {
        var who = outcome.Permission?.DisplayName ?? principal.Identifier;
        var role = ConnectionRoleNames.ToCli(outcome.Permission?.Role ?? requested);

        return outcome.Status == ConnectionOutcomeStatus.Unchanged
            ? $"{who} already has '{role}' on connection {outcome.ConnectionId}."
            : $"Connection {outcome.ConnectionId}: {who} {outcome.Status.ToKebabCase()} as '{role}'.";
    }
}
