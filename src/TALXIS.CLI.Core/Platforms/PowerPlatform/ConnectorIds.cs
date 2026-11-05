namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// The two spellings of a connector id: the short name
/// (<c>shared_office365users</c>) and the path form
/// (<c>/providers/Microsoft.PowerApps/apis/shared_office365users</c>).
/// </summary>
public static class ConnectorIds
{
    /// <summary>Prefix of the path-form connector id.</summary>
    public const string PathPrefix = "/providers/Microsoft.PowerApps/apis/";

    /// <summary>Reduces either form to the short name.</summary>
    /// <exception cref="ArgumentException"><paramref name="connector"/> is null or blank.</exception>
    public static string ToShort(string connector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        return LastSegment(connector);
    }

    /// <summary>Like <see cref="ToShort"/> for a stored value that may be absent: empty when there is none.</summary>
    public static string ToShortOrEmpty(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? string.Empty : LastSegment(stored);

    /// <summary>The path form, whichever form <paramref name="connector"/> is in.</summary>
    public static string ToPath(string connector) => PathPrefix + ToShort(connector);

    private static string LastSegment(string value)
    {
        var trimmed = value.Trim().TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }
}
