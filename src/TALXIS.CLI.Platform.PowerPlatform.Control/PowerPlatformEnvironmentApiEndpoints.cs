using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Single source of truth for the per-environment Power Platform API host
/// (<c>{prefix}.{shard}.environment.api.powerplatform.com</c>). Shared by the
/// copilot governance backend and the Power Automate connector metadata
/// client so the (non-obvious) hostname derivation lives in exactly one place.
/// </summary>
/// <remarks>
/// The host is derived from the environment ID, not discovered: the GUID is
/// lowercased and stripped of dashes, the first 30 hex characters form the
/// subdomain prefix and the last 2 form a shard label. Environments whose
/// platform name carries the <c>Default-</c> prefix additionally prepend
/// <c>default</c> to the prefix — hence the string overload, since a
/// <see cref="Guid"/> cannot carry that information.
/// </remarks>
internal static class PowerPlatformEnvironmentApiEndpoints
{
    private const string DefaultEnvironmentPrefix = "Default-";

    /// <summary>Builds the base URI for an environment identified by GUID.</summary>
    public static Uri BuildBaseUri(Guid environmentId, CloudInstance cloud)
        => BuildFromHex(environmentId.ToString("N"), isDefaultEnvironment: false, cloud);

    /// <summary>
    /// Builds the base URI from the platform's environment name, which is
    /// either a bare GUID or <c>Default-{guid}</c> for a tenant's default
    /// environment.
    /// </summary>
    public static Uri BuildBaseUri(string environmentName, CloudInstance cloud)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
            throw new ArgumentException("Environment name must not be empty.", nameof(environmentName));

        var trimmed = environmentName.Trim();
        var isDefault = trimmed.StartsWith(DefaultEnvironmentPrefix, StringComparison.OrdinalIgnoreCase);
        var candidate = isDefault ? trimmed[DefaultEnvironmentPrefix.Length..] : trimmed;

        if (!Guid.TryParse(candidate, out var environmentId))
        {
            throw new ArgumentException(
                $"Cannot derive a Power Platform environment API host from '{environmentName}': " +
                "expected a GUID, optionally prefixed with 'Default-'. Environment display names are not accepted.",
                nameof(environmentName));
        }

        return BuildFromHex(environmentId.ToString("N"), isDefault, cloud);
    }

    private static Uri BuildFromHex(string hex, bool isDefaultEnvironment, CloudInstance cloud)
    {
        // 32 hex chars split 30/2: the last two characters address the shard
        // the environment lives on and become their own DNS label.
        var prefix = (isDefaultEnvironment ? "default" : string.Empty) + hex[..^2];
        var shard = hex[^2..];
        return new Uri($"https://{prefix}.{shard}.{GetDomainSuffix(cloud)}/");
    }

    /// <summary>
    /// Resolves the per-environment API domain suffix for a sovereign cloud.
    /// Kept explicit so an unmapped cloud fails loudly rather than silently
    /// targeting the commercial host.
    /// </summary>
    /// <remarks>
    /// GCC intentionally shares the commercial host, preserving the mapping
    /// this type was extracted from and matching how
    /// <c>BapEndpointProvider</c> treats GCC for the BAP control plane.
    /// GCC High and DoD suffixes are inferred from the documented naming
    /// pattern and have not been exercised against a live sovereign tenant.
    /// </remarks>
    public static string GetDomainSuffix(CloudInstance cloud)
        => cloud switch
        {
            CloudInstance.Public or CloudInstance.Gcc => "environment.api.powerplatform.com",
            CloudInstance.GccHigh => "environment.api.high.powerplatform.microsoft.us",
            CloudInstance.Dod => "environment.api.appsplatform.us",
            _ => throw new NotSupportedException(
                $"The per-environment Power Platform API is not wired for cloud '{cloud}' in this release."),
        };
}
