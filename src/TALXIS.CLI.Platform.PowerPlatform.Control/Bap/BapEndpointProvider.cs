using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Bap;

/// <summary>
/// Single source of truth for Power Platform Business Application Platform
/// (BAP) admin API endpoints and constants. Centralised here so the
/// environment catalog (list/get) and the environment provisioner
/// (create + validation lookups) share one cloud→host map, token audience,
/// and API versions without duplication.
/// </summary>
internal static class BapEndpointProvider
{
    /// <summary>
    /// Token audience for the BAP admin API. The admin scope is acquired
    /// against the Power Apps service resource across all clouds.
    /// </summary>
    public static readonly Uri PowerAppsAudience = new("https://service.powerapps.com/");

    /// <summary>API version used by the environment list/get endpoints.</summary>
    public const string ListApiVersion = "2020-10-01";

    /// <summary>
    /// API version used by the environment create endpoint and the per-region
    /// currency/language/template validation lookups. Matches the version the
    /// Microsoft PAC CLI uses for the same calls.
    /// </summary>
    public const string CreateApiVersion = "2020-08-01";

    /// <summary>
    /// Resolves the BAP admin API base URI for the given sovereign cloud.
    /// </summary>
    /// <remarks>
    /// Public and GCC share the commercial host (GCC uses commercial identity
    /// for the BAP control plane); the sovereign clouds get their dedicated
    /// hosts. Kept intentionally explicit so an unmapped cloud fails loudly
    /// rather than silently targeting the wrong tenant.
    /// </remarks>
    public static Uri GetAdminApiBaseUri(CloudInstance cloud)
        => cloud switch
        {
            CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.bap.microsoft.com/"),
            CloudInstance.GccHigh => new Uri("https://high.api.bap.microsoft.us/"),
            CloudInstance.Dod => new Uri("https://api.bap.appsplatform.us/"),
            CloudInstance.China => new Uri("https://api.bap.partner.microsoftonline.cn/"),
            _ => throw new NotSupportedException(
                $"Power Platform environment administration is not wired for cloud '{cloud}' in this release."),
        };
}
