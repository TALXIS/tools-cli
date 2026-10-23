using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerApps;

/// <summary>
/// Endpoint constants for the Power Apps connector API (<c>api.powerapps.com</c>).
/// Kept intentionally explicit so an unmapped cloud fails loudly rather than
/// silently targeting the wrong host: the sovereign-cloud hosts differ from
/// the BAP admin API pattern (GCC does not share the commercial host here).
/// </summary>
internal static class PowerAppsEndpointProvider
{
    /// <summary>
    /// The connector API validates the same audience as the BAP admin API,
    /// so one cached token serves both.
    /// </summary>
    public static readonly Uri Audience = BapEndpointProvider.PowerAppsAudience;

    public const string ApiVersion = "2016-11-01";

    public static Uri GetApiBaseUri(CloudInstance cloud)
        => cloud switch
        {
            CloudInstance.Public => new Uri("https://api.powerapps.com/"),
            _ => throw new NotSupportedException(
                $"The Power Apps connector API is not wired for cloud '{cloud}' in this release."),
        };
}
