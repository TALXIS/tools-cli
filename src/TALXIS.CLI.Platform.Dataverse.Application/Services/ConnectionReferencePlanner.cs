using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// The decisions of the connection reference service that need no I/O: what a
/// create or update has to do, and how a binding is classified.
/// </summary>
internal static class ConnectionReferencePlanner
{
    /// <summary>Maps a connection's platform status to a binding status.</summary>
    public static string Classify(string? boundConnectionId, string? connectionStatus, bool connectionReadable)
    {
        if (string.IsNullOrWhiteSpace(boundConnectionId))
            return ConnectionBindingStatus.Unbound;
        if (!connectionReadable)
            return ConnectionBindingStatus.Unknown;
        return ConnectionStatusCodes.IsConnected(connectionStatus)
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
    /// The outcome of a create. An existing reference that was only added to the
    /// requested solution still counts as updated.
    /// </summary>
    public static ConnectionOutcomeStatus OutcomeOf(CreateAction action, bool addedToSolution) => action switch
    {
        CreateAction.Create => ConnectionOutcomeStatus.Created,
        CreateAction.Bind => ConnectionOutcomeStatus.Updated,
        _ => addedToSolution ? ConnectionOutcomeStatus.Updated : ConnectionOutcomeStatus.Unchanged,
    };

    /// <summary>True when an update would change nothing.</summary>
    public static bool IsNoOp(ConnectionReferenceRecord existing, string? connectionId, string? displayName)
        => (string.IsNullOrWhiteSpace(connectionId) || string.Equals(existing.ConnectionId, connectionId, StringComparison.Ordinal))
           && (string.IsNullOrWhiteSpace(displayName) || string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal));
}
