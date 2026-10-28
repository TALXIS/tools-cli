namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// What a connection, permission or connection reference operation did. The
/// values are part of the output contract; <see cref="ConnectionOutcomeStatusExtensions.ToKebabCase"/>
/// is the one place that names them.
/// </summary>
public enum ConnectionOutcomeStatus
{
    Created = 0,
    Updated = 1,
    Unchanged = 2,

    /// <summary>The connection exists but needs a person to complete consent.</summary>
    ConsentRequired = 3,

    /// <summary>The platform rejected the credentials.</summary>
    Failed = 4,

    /// <summary>A permission was granted.</summary>
    Assigned = 5,

    /// <summary>A permission was revoked.</summary>
    Removed = 6,

    Deleted = 7,

    /// <summary>A connection test passed.</summary>
    Healthy = 8,

    /// <summary>A connection test failed.</summary>
    Unhealthy = 9,

    /// <summary>The platform reports the connection as connected, but its test cannot be run for this credential.</summary>
    Untested = 10,
}

public static class ConnectionOutcomeStatusExtensions
{
    /// <summary>The status as written to JSON and text output, for example <c>consent-required</c>.</summary>
    public static string ToKebabCase(this ConnectionOutcomeStatus status) => status switch
    {
        ConnectionOutcomeStatus.Created => "created",
        ConnectionOutcomeStatus.Updated => "updated",
        ConnectionOutcomeStatus.Unchanged => "unchanged",
        ConnectionOutcomeStatus.ConsentRequired => "consent-required",
        ConnectionOutcomeStatus.Failed => "failed",
        ConnectionOutcomeStatus.Assigned => "assigned",
        ConnectionOutcomeStatus.Removed => "removed",
        ConnectionOutcomeStatus.Deleted => "deleted",
        ConnectionOutcomeStatus.Healthy => "healthy",
        ConnectionOutcomeStatus.Unhealthy => "unhealthy",
        ConnectionOutcomeStatus.Untested => "untested",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown connection outcome status."),
    };
}
