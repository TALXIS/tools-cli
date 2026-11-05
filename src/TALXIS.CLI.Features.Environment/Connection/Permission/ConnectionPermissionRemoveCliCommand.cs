using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// Stops sharing a connection.
/// Usage: <c>txc environment connection permission remove &lt;connection-id&gt; [--connector &lt;name&gt;] --user|--service-principal|--group &lt;id&gt; [--yes]</c>
/// </summary>
[CliDestructive("Revokes the principal's access to the connection. Flows or connection references the principal bound to it stop working for them.")]
[CliCommand(
    Name = "remove",
    Description = "Stops sharing a connection with a user, service principal or group in the LIVE connected environment. Requires an active profile. The owner cannot be removed."
)]
public class ConnectionPermissionRemoveCliCommand : ConnectionPrincipalCommandBase, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionPermissionRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!TryBuildPrincipal(out var principal))
            return ExitValidationError;

        var service = TxcServices.Get<IConnectionPermissionService>();
        var outcome = await service.RemovePermissionAsync(
            Profile, Connector, ConnectionId, principal, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                status = outcome.Status.ToKebabCase(),
                connectionId = outcome.ConnectionId,
                apiId = outcome.ApiId,
                permission = outcome.Permission is { } p ? ConnectionPermissionListCliCommand.ToRow(p) : null,
            },
            _ => OutputWriter.WriteLine(Describe(outcome, principal)));

        return ExitSuccess;
    }

    private static string Describe(ConnectionPermissionOutcome outcome, ConnectionPrincipalSpec principal)
        => outcome.Status == ConnectionOutcomeStatus.Unchanged
            ? $"{principal.Identifier} had no access to connection {outcome.ConnectionId}."
            : $"Removed {outcome.Permission?.DisplayName ?? principal.Identifier} from connection {outcome.ConnectionId}.";
}
