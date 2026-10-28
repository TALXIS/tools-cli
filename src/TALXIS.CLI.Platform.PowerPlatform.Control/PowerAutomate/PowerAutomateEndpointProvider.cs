using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;

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
    public static readonly Uri Audience = BapEndpointProvider.PowerAppsAudience;

    public const string ApiVersion = "1";

    /// <summary>
    /// Hides connectors that only exist for M365 Copilot, matching what the
    /// Power Automate designer sends.
    /// </summary>
    private const string ConnectorHideKey = "M365Copilot";

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
    /// For an operation whose lookups only work when the caller supplies the
    /// environment implicitly, the twin operation whose lookups declare it, and
    /// the name of that declared parameter.
    /// </summary>
    /// <remarks>
    /// Verified live on the "current environment" Dataverse connector: its own
    /// lookups answer <c>Invalid organization URL 'null'</c>, and even with the
    /// environment passed as a header its <c>$metadata.json</c> routes return
    /// 404 — the designer relies on routing it does not publish. Every such
    /// operation has a <c>...WithOrganization</c> twin with the same parameters
    /// whose lookups take the environment as a declared <c>organization</c>
    /// header, and those resolve when called directly.
    /// </remarks>
    public static (string Operation, string EnvironmentParameter)? EnvironmentScopedTwin(string connector, string operation)
        => string.Equals(connector, "shared_commondataserviceforapps", StringComparison.OrdinalIgnoreCase)
            && !operation.EndsWith("WithOrganization", StringComparison.Ordinal)
                ? (operation + "WithOrganization", "organization")
                : null;
}
