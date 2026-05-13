namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// URL builders for the Power Apps UCI runtime (<c>{org}.crm{N}.dynamics.com/main.aspx</c>).
/// Covers model-driven app shell, all UCI page types (entityrecord, entitylist, dashboard,
/// webresource, control, custom, inlinedialog, genux, search), SCF record forms, and reports.
/// </summary>
public static class PowerAppsUciUrls
{
    /// <summary>Open a model-driven app by its unique name.</summary>
    public static Uri AppByName(string orgUrl, string uniqueName)
        => new($"https://{BrowseUrlConstants.NormalizeOrgUrl(orgUrl)}/main.aspx?appname={Uri.EscapeDataString(uniqueName)}");

    /// <summary>Open a model-driven app by its AppModuleId GUID.</summary>
    public static Uri AppById(string orgUrl, Guid appModuleId)
        => new($"https://{BrowseUrlConstants.NormalizeOrgUrl(orgUrl)}/main.aspx?appid={appModuleId}");

    /// <summary>
    /// Generic deep-link into a model-driven app via <c>main.aspx</c>.
    /// Builds the URL from app identity + pagetype + arbitrary query parameters.
    /// Supports all UCI page types: entityrecord, entitylist, dashboard, webresource,
    /// control, custom, inlinedialog, genux, search, apps.
    /// </summary>
    public static Uri DeepLink(string orgUrl, string? appName, Guid? appId, string pageType, IDictionary<string, string> queryParams)
    {
        var org = BrowseUrlConstants.NormalizeOrgUrl(orgUrl);
        var qs = new List<string>();

        if (!string.IsNullOrWhiteSpace(appName))
            qs.Add($"appname={Uri.EscapeDataString(appName)}");
        else if (appId.HasValue)
            qs.Add($"appid={appId.Value}");

        qs.Add($"pagetype={Uri.EscapeDataString(pageType)}");

        foreach (var (key, value) in queryParams)
            qs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");

        return new Uri($"https://{org}/main.aspx?{string.Join("&", qs)}");
    }

    /// <summary>
    /// Open an SCF or unrecognized component type's backing entity record form.
    /// </summary>
    public static Uri RecordForm(string orgUrl, string entityLogicalName, Guid recordId)
        => new($"https://{BrowseUrlConstants.NormalizeOrgUrl(orgUrl)}/main.aspx?forceUCI=1&newWindow=true&pagetype=entityrecord&etn={entityLogicalName}&id={recordId}");

    /// <summary>Open a report in the Dynamics report viewer.</summary>
    public static Uri Report(string orgUrl, Guid reportId, string action = "run")
        => new($"https://{BrowseUrlConstants.NormalizeOrgUrl(orgUrl)}/crmreports/viewer/viewer.aspx?action={Uri.EscapeDataString(action)}&id=%7b{reportId}%7d");
}
