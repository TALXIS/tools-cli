using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Token audience and URL builders for the Power Automate metadata routes of
/// the per-environment Power Platform API
/// (<c>{env}.environment.api.powerplatform.com/powerautomate/...</c>).
/// </summary>
internal static class PowerAutomateEndpointProvider
{
    /// <summary>
    /// Token audience for the <c>/powerautomate/*</c> routes.
    /// </summary>
    /// <remarks>
    /// Verified against a live tenant: these routes accept a Power Apps service
    /// token and reject a Power Platform API token with 401, although the latter
    /// works for other routes on the same host. It is also the audience the BAP
    /// admin API uses, so the cached token is shared. The Flow service resource
    /// Microsoft's own tooling uses cannot be issued to the pinned public client
    /// (AADSTS65002).
    /// </remarks>
    public static readonly Uri Audience = new("https://service.powerapps.com/");

    /// <summary>
    /// Scope for calling a connector's runtime (<c>…azure-apihub.net/apim/{connector}</c>)
    /// through a connection, which is how design-time lookups are made.
    /// </summary>
    /// <remarks>
    /// Verified live: the runtime's token exchange allows the audience
    /// <c>https://apihub.azure.com</c> but rejects <c>https://apihub.azure.com/</c>,
    /// which is what the <c>{resource}//.default</c> form would produce.
    /// </remarks>
    public const string ConnectorRuntimeScope = "https://apihub.azure.com/.default";

    public const string ApiVersion = "1";

    private const string PowerAppsApiVersion = "2016-11-01";

    /// <summary>
    /// Hides connectors that only exist for M365 Copilot, matching what the
    /// Power Automate designer sends.
    /// </summary>
    private const string ConnectorHideKey = "M365Copilot";

    /// <summary><c>/powerautomate/apis</c> — the connector catalog.</summary>
    public static Uri Connectors(Uri baseUri)
        => new(baseUri, $"powerautomate/apis?api-version={ApiVersion}");

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
    /// with the input and response definitions — the designer's parameter spec.
    /// </summary>
    public static Uri Operation(Uri baseUri, string connector, string operation)
        => new(baseUri,
            $"powerautomate/apis/{Uri.EscapeDataString(connector)}/apiOperations/{Uri.EscapeDataString(operation)}" +
            $"?api-version={ApiVersion}&addConnectorHideKey={ConnectorHideKey}" +
            "&$expand=properties/inputsDefinition,properties/responsesDefinition,properties/connector");

    /// <summary>
    /// A connection on the Power Apps API, which reports its owner and status;
    /// the per-environment host does not expose single connections with this audience.
    /// </summary>
    public static Uri Connection(CloudInstance cloud, Guid environmentId, string connector, string connectionId)
    {
        var host = cloud switch
        {
            CloudInstance.Public => "https://api.powerapps.com/",
            _ => throw new NotSupportedException(
                $"Resolving connector lookups through a connection is not wired for cloud '{cloud}' in this release."),
        };

        var filter = Uri.EscapeDataString($"environment eq '{environmentId:D}'");
        return new Uri(new Uri(host),
            $"providers/Microsoft.PowerApps/apis/{Uri.EscapeDataString(connector)}/connections/{Uri.EscapeDataString(connectionId)}" +
            $"?api-version={PowerAppsApiVersion}&$filter={filter}");
    }

    /// <summary>The id a flow definition uses to reference a connector.</summary>
    public static string BuildApiId(string connector)
        => $"/providers/Microsoft.PowerApps/apis/{connector}";
}
