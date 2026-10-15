using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// The decisions of the connection reference service that need no I/O: what a
/// create or update has to do, and how a binding is classified.
/// </summary>
internal static class ConnectionReferencePlanner
{
    public const string PathPrefix = "/providers/Microsoft.PowerApps/apis/";

    /// <summary>Dataverse stores the connector as the path-form apiId.</summary>
    public static string ToStoredConnectorId(string shortApiId) => PathPrefix + shortApiId;

    public static string ToShortApiId(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return string.Empty;
        var trimmed = stored.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    /// <summary>Maps a connection's platform status to a binding status.</summary>
    public static string Classify(string? boundConnectionId, string? connectionStatus, bool connectionReadable)
    {
        if (string.IsNullOrWhiteSpace(boundConnectionId))
            return ConnectionBindingStatus.Unbound;
        if (!connectionReadable)
            return ConnectionBindingStatus.Unknown;
        return string.Equals(connectionStatus, "Connected", StringComparison.OrdinalIgnoreCase)
            ? ConnectionBindingStatus.Bound
            : ConnectionBindingStatus.Broken;
    }

    public enum CreateAction { Create = 0, Unchanged = 1, Bind = 2 }

    /// <summary>
    /// What a create must do when a reference with that logical name may already
    /// exist. A reference for a different connector is a conflict, not an update.
    /// </summary>
    /// <exception cref="ArgumentException">The logical name is taken by a reference for another connector.</exception>
    public static CreateAction PlanCreate(
        ConnectionReferenceRecord? existing, string requestedApiId, string? requestedConnectionId)
    {
        if (existing is null)
            return CreateAction.Create;

        if (!string.Equals(existing.ApiId, requestedApiId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"A connection reference named '{existing.LogicalName}' already exists for connector '{existing.ApiId}', not '{requestedApiId}'.");
        }

        return !string.IsNullOrWhiteSpace(requestedConnectionId)
            && !string.Equals(existing.ConnectionId, requestedConnectionId, StringComparison.Ordinal)
                ? CreateAction.Bind
                : CreateAction.Unchanged;
    }

    /// <summary>
    /// Turns the platform's binding failures into actionable messages. Binding is
    /// validated as the caller, so the two failures people hit are a connection id
    /// that does not exist and a connection they have not been given access to.
    /// </summary>
    /// <returns>True when <paramref name="exception"/> was a binding failure.</returns>
    public static bool TryTranslateBindingFailure(
        Exception exception, string? connectionId, string apiId, out Exception translated)
    {
        translated = exception;
        var text = exception.ToString();

        if (text.Contains("ConnectionNotFound", StringComparison.Ordinal))
        {
            translated = new ArgumentException(
                $"Connection '{connectionId}' was not found for connector '{apiId}'. " +
                $"List the connections you can use with: txc environment connection list --connector {apiId}. " +
                "A connection created by another identity is only visible once it is shared with you.");
            return true;
        }

        if (text.Contains("ConnectionAuthorizationFailed", StringComparison.Ordinal))
        {
            translated = new InvalidOperationException(
                $"You do not have permission to use connection '{connectionId}'. Ask its owner to share it with you: " +
                $"txc environment connection permission add {connectionId} --connector {apiId} --service-principal <client id> (or --user <upn>).");
            return true;
        }

        return false;
    }

    /// <summary>True when an update would change nothing.</summary>
    public static bool IsNoOp(ConnectionReferenceRecord existing, string? connectionId, string? displayName)
        => (string.IsNullOrWhiteSpace(connectionId) || string.Equals(existing.ConnectionId, connectionId, StringComparison.Ordinal))
           && (string.IsNullOrWhiteSpace(displayName) || string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal));
}
