using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Single source of truth for the per-environment Power Platform API host
/// (<c>{prefix}.{shard}.environment.api.powerplatform.com</c>). Shared by the
/// copilot governance backend and the Power Automate connector metadata
/// client so the non-obvious hostname derivation lives in one place.
/// </summary>
/// <remarks>
/// The host is derived from the environment ID: the GUID is lowercased and
/// stripped of dashes, the first 30 hex characters form the subdomain prefix
/// and the last 2 form a shard label.
/// </remarks>
internal static class PowerPlatformEnvironmentApiEndpoints
{
    public static Uri BuildBaseUri(Guid environmentId, CloudInstance cloud)
    {
        var hex = environmentId.ToString("N");
        return new Uri($"https://{hex[..^2]}.{hex[^2..]}.{GetDomainSuffix(cloud)}/");
    }

    /// <summary>
    /// Only the clouds whose host has been exercised are mapped; anything else
    /// fails loudly rather than targeting a guessed host.
    /// </summary>
    private static string GetDomainSuffix(CloudInstance cloud)
        => cloud switch
        {
            CloudInstance.Public or CloudInstance.Gcc => "environment.api.powerplatform.com",
            _ => throw new NotSupportedException(
                $"The per-environment Power Platform API is not wired for cloud '{cloud}' in this release."),
        };
}
