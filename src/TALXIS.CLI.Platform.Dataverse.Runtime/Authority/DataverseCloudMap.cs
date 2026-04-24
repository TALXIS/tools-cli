using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.Authority;

/// <summary>
/// Infers a <see cref="CloudInstance"/> from a Dataverse environment URL
/// by matching well-known host suffixes. Generic Entra authority mapping
/// now lives in <see cref="TALXIS.CLI.Core.Identity.EntraCloudMap"/>.
/// </summary>
public static class DataverseCloudMap
{
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
