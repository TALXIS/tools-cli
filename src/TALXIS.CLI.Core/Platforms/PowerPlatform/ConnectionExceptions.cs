namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>The connection does not exist, or is not visible to the caller.</summary>
public sealed class ConnectionNotFoundException : ArgumentException
{
    /// <param name="apiId">The connector the connection was looked up under; null when the caller did not name one.</param>
    public ConnectionNotFoundException(string connectionId, string? apiId = null)
        : base((apiId is null
                    ? $"Connection '{connectionId}' was not found. "
                    : $"Connection '{connectionId}' was not found for connector '{apiId}'. ")
               + "A connection created by another identity is only visible once it is shared with you.")
    {
        ConnectionId = connectionId;
        ApiId = apiId;
    }

    public string ConnectionId { get; }

    public string? ApiId { get; }
}

/// <summary>The same connection id exists under more than one connector, so the caller must say which.</summary>
public sealed class AmbiguousConnectionException : ArgumentException
{
    public AmbiguousConnectionException(string connectionId, IReadOnlyList<string> apiIds)
        : base($"Connection id '{connectionId}' exists for {apiIds.Count} connectors: {string.Join(", ", apiIds)}. " +
               "Name the connector to choose one.")
    {
        ConnectionId = connectionId;
        ApiIds = apiIds;
    }

    public string ConnectionId { get; }

    /// <summary>The apiIds of the connectors that have a connection with this id.</summary>
    public IReadOnlyList<string> ApiIds { get; }
}

/// <summary>The connection exists but the caller may not use it.</summary>
public sealed class ConnectionAccessDeniedException : ArgumentException
{
    public ConnectionAccessDeniedException(string connectionId, string apiId)
        : base($"You do not have permission to use connection '{connectionId}' for connector '{apiId}'. " +
               "It has to be shared with the calling identity.")
    {
        ConnectionId = connectionId;
        ApiId = apiId;
    }

    public string ConnectionId { get; }

    public string ApiId { get; }
}
