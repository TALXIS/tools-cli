using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Identity;

/// <summary>
/// Maps <see cref="CloudInstance"/> to its Microsoft Entra authority host
/// and builds full MSAL authority URIs.
/// </summary>
/// <remarks>
/// Host/authority constants mirror <c>bolt.authentication.AuthorityInfo</c>
/// in pac 2.6.3 (see <c>temp/pac-auth-research.md</c>). The Public authority is
/// also used for Preprod / Test tenants; we don't split those for v1.
/// </remarks>
public static class EntraCloudMap
{
    public const string PublicAuthority  = "https://login.microsoftonline.com";
    public const string UsGovAuthority   = "https://login.microsoftonline.us";
    public const string ChinaAuthority   = "https://login.partner.microsoftonline.cn";

    /// <summary>Entra authority host for the given cloud, without trailing slash or tenant segment.</summary>
    public static string GetAuthorityHost(CloudInstance cloud) => cloud switch
    {
        CloudInstance.Public  => PublicAuthority,
        CloudInstance.Gcc     => PublicAuthority,
        CloudInstance.GccHigh => UsGovAuthority,
        CloudInstance.Dod     => UsGovAuthority,
        CloudInstance.China   => ChinaAuthority,
        _ => throw new ArgumentOutOfRangeException(nameof(cloud), cloud, "Unknown cloud instance."),
    };

    /// <summary>
    /// Returns the full MSAL authority URI for the cloud.
    /// If <paramref name="tenantId"/> is provided it's appended as the
    /// directory segment; otherwise <c>organizations</c> is used so MSAL can
    /// resolve the tenant at login time.
    /// </summary>
    public static Uri BuildAuthorityUri(CloudInstance cloud, string? tenantId)
    {
        var host = GetAuthorityHost(cloud);
        var directory = string.IsNullOrWhiteSpace(tenantId) ? "organizations" : tenantId.Trim();
        return new Uri($"{host}/{directory}");
    }
}
