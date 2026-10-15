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
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown connection outcome status."),
    };
}
