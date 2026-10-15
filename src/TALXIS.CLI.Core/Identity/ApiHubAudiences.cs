using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Identity;

/// <summary>
/// Token audiences of the Azure API Hub (connector runtime and consent service)
/// per cloud.
/// </summary>
/// <remarks>
/// The Public value is verified against a live tenant: a user token whose
/// audience is exactly <c>https://apihub.azure.com</c> (scope
/// <c>https://apihub.azure.com/.default</c>, single slash) completes the
/// first-party consent login and is accepted by connection test links. The
/// sovereign values mirror the Microsoft Power Apps and Managed Apps CLIs and
/// have not been verified here.
/// </remarks>
public static class ApiHubAudiences
{
    public const string Public = "https://apihub.azure.com";
    public const string UsGovCommercial = "https://power-apis-usgov001-public.azure-apihub.us";
    public const string UsGovHigh = "https://power-apis-usgov001-sovereign.azure-apihub.us";
    public const string UsGovDod = "https://power-apis-usdod-001.azure-apihub.us";
    public const string China = "https://china.azure-apihub.cn";

    private static readonly HashSet<string> KnownHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        new Uri(Public).Host,
        new Uri(UsGovCommercial).Host,
        new Uri(UsGovHigh).Host,
        new Uri(UsGovDod).Host,
        new Uri(China).Host,
    };

    /// <summary>API Hub audience for the given cloud.</summary>
    public static Uri For(CloudInstance cloud) => new(cloud switch
    {
        CloudInstance.Public => Public,
        CloudInstance.Gcc => UsGovCommercial,
        CloudInstance.GccHigh => UsGovHigh,
        CloudInstance.Dod => UsGovDod,
        CloudInstance.China => China,
        _ => throw new ArgumentOutOfRangeException(nameof(cloud), cloud, "Unknown cloud instance."),
    });

    /// <summary>
    /// True when <paramref name="resource"/> is one of the API Hub audiences.
    /// Those resources expect a single-slash <c>/.default</c> scope, unlike
    /// Dataverse which needs <c>//.default</c>.
    /// </summary>
    public static bool IsApiHub(Uri resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.IsAbsoluteUri && KnownHosts.Contains(resource.Host);
    }
}
