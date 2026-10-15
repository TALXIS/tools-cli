namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>The connection does not exist, or is not visible to the caller.</summary>
public sealed class ConnectionNotFoundException : ArgumentException
{
    public ConnectionNotFoundException(string connectionId, string apiId)
        : base($"Connection '{connectionId}' was not found for connector '{apiId}'. " +
               "A connection created by another identity is only visible once it is shared with you.")
    {
        ConnectionId = connectionId;
        ApiId = apiId;
    }

    public string ConnectionId { get; }

    public string ApiId { get; }
}

/// <summary>The connection exists but the caller may not use it.</summary>
public sealed class ConnectionAccessDeniedException : ArgumentException
{
    public ConnectionAccessDeniedException(string connectionId, string apiId)
        : base($"You do not have permission to use connection '{connectionId}' for connector '{apiId}'. " +
               "Ask its owner to share it with the calling identity.")
    {
        ConnectionId = connectionId;
        ApiId = apiId;
    }

    public string ConnectionId { get; }

    public string ApiId { get; }
}
