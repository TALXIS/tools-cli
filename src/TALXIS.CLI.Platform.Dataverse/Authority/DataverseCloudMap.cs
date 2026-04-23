using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.Dataverse.Authority;

/// <summary>
/// Maps <see cref="CloudInstance"/> to its Microsoft Entra authority host
/// and infers the cloud from a Dataverse environment URL host suffix.
/// </summary>
/// <remarks>
/// Host/authority constants mirror <c>bolt.authentication.AuthorityInfo</c>
/// in pac 2.6.3 (see <c>temp/pac-auth-research.md</c>). The Public authority is
/// also used for Preprod / Test tenants; we don't split those for v1.
/// </remarks>
public static class DataverseCloudMap
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

    /// <summary>
    /// Infers a <see cref="CloudInstance"/> from a Dataverse environment URL
    /// by matching well-known host suffixes. Returns <c>null</c> when the host
    /// does not match any known sovereign pattern (caller should fall back to
    /// <see cref="CloudInstance.Public"/> or the value stored on the Connection).
    /// </summary>
    public static CloudInstance? TryInferFromEnvironmentUrl(Uri environmentUrl)
    {
        ArgumentNullException.ThrowIfNull(environmentUrl);
        var host = environmentUrl.Host.ToLowerInvariant();

        // DoD → *.crm.appsplatform.us ; GccHigh → *.crm.microsoftdynamics.us ; Gcc → *.crm9.dynamics.com ; China → *.crm.dynamics.cn
        if (host.EndsWith(".crm.appsplatform.us", StringComparison.Ordinal))
            return CloudInstance.Dod;
        if (host.EndsWith(".crm.microsoftdynamics.us", StringComparison.Ordinal) ||
            host.EndsWith(".crm.dynamics.us", StringComparison.Ordinal))
            return CloudInstance.GccHigh;
        if (host.EndsWith(".crm9.dynamics.com", StringComparison.Ordinal))
            return CloudInstance.Gcc;
        if (host.EndsWith(".crm.dynamics.cn", StringComparison.Ordinal))
            return CloudInstance.China;
        if (host.EndsWith(".dynamics.com", StringComparison.Ordinal))
            return CloudInstance.Public;

        return null;
    }
}
