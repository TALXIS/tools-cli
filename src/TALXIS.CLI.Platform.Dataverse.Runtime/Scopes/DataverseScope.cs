namespace TALXIS.CLI.Platform.Dataverse.Runtime.Scopes;

/// <summary>
/// Builds the Dataverse MSAL scope string.
/// </summary>
/// <remarks>
/// Dataverse requires a <c>//.default</c> suffix (double-slash, one slash
/// more than most Azure services). This matches pac CLI's
/// <c>useDoubleSlashScopeSeparator = true</c> default and is mandatory for
/// CRM audience matching — see <c>temp/pac-auth-research.md</c>.
/// </remarks>
public static class DataverseScope
{
    public const string DefaultSuffix = "//.default";

    public static string BuildDefault(Uri environmentUrl)
    {
        ArgumentNullException.ThrowIfNull(environmentUrl);
        return environmentUrl.GetLeftPart(UriPartial.Authority) + DefaultSuffix;
    }
}
