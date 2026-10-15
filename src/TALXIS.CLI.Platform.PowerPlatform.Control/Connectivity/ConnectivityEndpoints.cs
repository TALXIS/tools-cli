namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// URL builders for the <c>/connectivity/*</c> surface of the per-environment
/// Power Platform API (<c>{env}.environment.api.powerplatform.com</c>).
/// </summary>
/// <remarks>
/// Verified against a live tenant: every route here takes api-version 1 and an
/// <c>environment eq '{id}'</c> filter, and accepts a Power Platform API token
/// (audience <c>https://api.powerplatform.com</c>).
/// </remarks>
internal static class ConnectivityEndpoints
{
    public const string ApiVersion = "1";

    /// <summary>The audience every connectivity route accepts.</summary>
    public static readonly Uri Audience = new("https://api.powerplatform.com/");

    /// <summary>Prefix of the path-form connector id.</summary>
    public const string ApiIdPrefix = "/providers/Microsoft.PowerApps/apis/";

    /// <summary>
    /// Reduces a connector reference to the short name the connectivity routes
    /// take. Accepts the path form <c>/providers/Microsoft.PowerApps/apis/shared_x</c>.
    /// </summary>
    public static string ToShortApiId(string connector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        var trimmed = connector.Trim().TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    public static string ToPathApiId(string shortApiId) => ApiIdPrefix + ToShortApiId(shortApiId);

    /// <summary><c>/connectivity/connectors</c> — the connector catalog.</summary>
    public static Uri Connectors(Uri baseUri, Guid environmentId)
        => new(baseUri, $"connectivity/connectors?api-version={ApiVersion}&{EnvironmentFilter(environmentId)}");

    /// <summary><c>/connectivity/connectors/{apiId}</c> — one connector with its connection parameter sets.</summary>
    public static Uri Connector(Uri baseUri, string apiId, Guid environmentId)
        => new(baseUri, $"connectivity/connectors/{Segment(apiId)}?api-version={ApiVersion}&{EnvironmentFilter(environmentId)}");

    /// <summary>
    /// <c>/connectivity/connections</c> for the caller. A connection created by
    /// another identity (notably a service principal) is only returned when the
    /// permissions expand names the caller's object id, so it is always sent.
    /// </summary>
    public static Uri Connections(Uri baseUri, Guid environmentId, string callerObjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerObjectId);
        var expand = Uri.EscapeDataString($"permissions($filter=maxAssignedTo('{callerObjectId}'))");
        return new Uri(baseUri,
            $"connectivity/connections?api-version={ApiVersion}&{EnvironmentFilter(environmentId)}&$expand={expand}");
    }

    /// <summary>
    /// <c>/connectivity/connectors/{apiId}/connections/{id}</c>. With
    /// <paramref name="expandConsent"/> the response carries the consent link;
    /// the platform then requires a redirect URL in the request body.
    /// </summary>
    public static Uri Connection(Uri baseUri, string apiId, string connectionId, bool expandConsent)
        => new(baseUri,
            $"connectivity/connectors/{Segment(apiId)}/connections/{Segment(connectionId)}?api-version={ApiVersion}" +
            (expandConsent ? "&$expand=ConsentLink" : string.Empty));

    /// <summary>Same resource, addressed for GET and DELETE with the environment filter.</summary>
    public static Uri ConnectionInEnvironment(Uri baseUri, string apiId, string connectionId, Guid environmentId)
        => new(baseUri,
            $"connectivity/connectors/{Segment(apiId)}/connections/{Segment(connectionId)}?api-version={ApiVersion}&{EnvironmentFilter(environmentId)}");

    /// <summary><c>.../connections/{id}/permissions</c> — who can use the connection.</summary>
    public static Uri Permissions(Uri baseUri, string apiId, string connectionId, Guid environmentId)
        => new(baseUri,
            $"connectivity/connectors/{Segment(apiId)}/connections/{Segment(connectionId)}/permissions?api-version={ApiVersion}&{EnvironmentFilter(environmentId)}");

    /// <summary><c>.../connections/{id}/modifyPermissions</c> — grant and revoke (POST).</summary>
    public static Uri ModifyPermissions(Uri baseUri, string apiId, string connectionId, Guid environmentId)
        => new(baseUri,
            $"connectivity/connectors/{Segment(apiId)}/connections/{Segment(connectionId)}/modifyPermissions?api-version={ApiVersion}&{EnvironmentFilter(environmentId)}");

    private static string EnvironmentFilter(Guid environmentId)
        => "$filter=" + Uri.EscapeDataString($"environment eq '{environmentId}'");

    private static string Segment(string value) => Uri.EscapeDataString(value);
}
