using TALXIS.CLI.Core.Identity;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.Scopes;

/// <summary>
/// Chooses the MSAL scope for an arbitrary resource URI.
/// </summary>
/// <remarks>
/// Dataverse needs the double-slash <c>//.default</c> separator (see
/// <see cref="DataverseScope"/>). API Hub resources take the single-slash form
/// (see <see cref="ApiHubAudiences"/>). Other resources keep the existing behaviour.
/// </remarks>
public static class ResourceScope
{
    public const string SingleSlashSuffix = "/.default";

    public static string Build(Uri resourceUri)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);

        return ApiHubAudiences.IsApiHub(resourceUri)
            ? resourceUri.GetLeftPart(UriPartial.Authority) + SingleSlashSuffix
            : DataverseScope.BuildDefault(resourceUri);
    }
}
