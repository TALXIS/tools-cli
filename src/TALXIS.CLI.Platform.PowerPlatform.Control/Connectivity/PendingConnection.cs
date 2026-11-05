using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// A connection a create call has written and is still settling: it names the
/// outcome to report once the connection turns out healthy.
/// </summary>
internal sealed record PendingConnection(
    ResolvedConnectorInfo Connector,
    string ConnectionId,
    string DisplayName,
    ConnectionOutcomeStatus HealthyStatus)
{
    public ConnectionCreateOutcome Outcome(
        ConnectionOutcomeStatus status, string? connectionStatus, string? consentLink, string? message)
        => new(status, ConnectionId, Connector.ApiId, DisplayName, connectionStatus, consentLink, message);
}
