namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// Shared constants for browse URL construction.
/// </summary>
public static class BrowseUrlConstants
{
    /// <summary>Default Solution GUID — used when no solution context is specified for form/view/securityrole.</summary>
    public const string DefaultSolutionId = "fd140aaf-4df4-11dd-bd17-0019b9312238";

    /// <summary>
    /// Extracts the hostname from an org URL for use in URL construction.
    /// Accepts either a full URL or a bare hostname.
    /// </summary>
    public static string NormalizeOrgUrl(string orgUrl)
    {
        if (Uri.TryCreate(orgUrl, UriKind.Absolute, out var uri))
            return uri.Host;
        // Already a bare hostname
        return orgUrl.TrimEnd('/');
    }
}
