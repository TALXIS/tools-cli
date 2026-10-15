namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Token audiences and URL builders for the Power Automate metadata surface
/// of the per-environment Power Platform API
/// (<c>{env}.environment.api.powerplatform.com/powerautomate/...</c>).
/// </summary>
internal static class PowerAutomateEndpointProvider
{
    /// <summary>
    /// Primary audience for the Power Automate metadata endpoints.
    /// </summary>
    /// <remarks>
    /// Verified against a live tenant: the <c>/powerautomate/*</c> routes accept
    /// a Power Apps service token and reject a Power Platform API token with
    /// 401, even though the latter works for other routes on the same host.
    /// This resource is also one the pinned pac application can obtain, unlike
    /// the Flow service.
    /// </remarks>
    public static readonly Uri PowerAppsServiceAudience = new("https://service.powerapps.com/");

    /// <summary>
    /// Second choice. Correct for the <c>/connectivity/*</c> routes and other
    /// surfaces on the same host.
    /// </summary>
    public static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");

    public const string ApiVersion = "1";

    /// <summary>
    /// Hides connectors that only exist for M365 Copilot from the operation
    /// catalog, matching what the Power Automate designer sends.
    /// </summary>
    private const string ConnectorHideKey = "M365Copilot";

    /// <summary><c>/powerautomate/apis</c> — the connector catalog.</summary>
    public static Uri Connectors(Uri baseUri, int? top)
    {
        var query = $"api-version={ApiVersion}";
        if (top is > 0)
            query += $"&$top={top.Value}";
        return new Uri(baseUri, $"powerautomate/apis?{query}");
    }

    /// <summary>
    /// <c>/powerautomate/apis/{connector}</c> expanded with the connector's
    /// Swagger document, which carries the operation index.
    /// </summary>
    public static Uri Connector(Uri baseUri, string connector)
        => new(baseUri, $"powerautomate/apis/{Uri.EscapeDataString(connector)}?api-version={ApiVersion}&$expand=swagger");

    /// <summary><c>/powerautomate/operations</c> — cross-connector operation search (POST).</summary>
    public static Uri OperationSearch(Uri baseUri)
        => new(baseUri, $"powerautomate/operations?api-version={ApiVersion}&addConnectorHideKey={ConnectorHideKey}");

    /// <summary>
    /// <c>/powerautomate/apis/{connector}/apiOperations/{operation}</c> expanded
    /// with the input and response definitions — the authoritative parameter spec.
    /// </summary>
    public static Uri OperationSchema(Uri baseUri, string connector, string operation)
        => new(baseUri,
            $"powerautomate/apis/{Uri.EscapeDataString(connector)}/apiOperations/{Uri.EscapeDataString(operation)}" +
            $"?api-version={ApiVersion}&addConnectorHideKey={ConnectorHideKey}" +
            "&$expand=properties/inputsDefinition,properties/responsesDefinition,properties/connector");

    /// <summary><c>/connectivity/connections</c> — connections, optionally per connector.</summary>
    public static Uri Connections(Uri baseUri, string? connector)
        => string.IsNullOrWhiteSpace(connector)
            ? new Uri(baseUri, $"connectivity/connections?api-version={ApiVersion}")
            : new Uri(baseUri, $"connectivity/connectors/{Uri.EscapeDataString(connector)}/connections?api-version={ApiVersion}");

    /// <summary>The platform id a flow definition uses to reference a connector.</summary>
    public static string BuildApiId(string connector)
        => $"/providers/Microsoft.PowerApps/apis/{connector}";

    /// <summary>
    /// Extracts the connector name from an <c>apiId</c> such as
    /// <c>/providers/Microsoft.PowerApps/apis/shared_teams</c>. Returns
    /// <see langword="null"/> when the value is not a connector reference.
    /// </summary>
    public static string? TryParseConnectorFromApiId(string? apiId)
    {
        if (string.IsNullOrWhiteSpace(apiId))
            return null;

        var trimmed = apiId.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == trimmed.Length - 1)
            return null;

        var name = trimmed[(lastSlash + 1)..];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
