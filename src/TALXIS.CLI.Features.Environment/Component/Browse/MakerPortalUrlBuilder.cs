using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// Builds Power Platform maker portal URLs for component types.
/// Each method corresponds to a specific component editor URL pattern.
/// </summary>
public static class MakerPortalUrlBuilder
{
    /// <summary>Default Solution GUID used when no solution context is specified for form/view/securityrole.</summary>
    public const string DefaultSolutionId = "fd140aaf-4df4-11dd-bd17-0019b9312238";

    public static Uri Solution(Guid environmentId, Guid solutionId)
        => new($"https://make.powerapps.com/environments/{environmentId}/solutions/{solutionId}");

    public static Uri Entity(Guid environmentId, Guid metadataId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"https://make.powerapps.com/environments/{environmentId}/solutions/{solutionId}/entities/{metadataId}/fields")
            : new($"https://make.powerapps.com/environments/{environmentId}/entities/{metadataId}/fields");

    public static Uri Form(Guid environmentId, string entityLogicalName, Guid formId, Guid? solutionId = null)
    {
        var slnId = solutionId ?? Guid.Parse(DefaultSolutionId);
        return new($"https://make.powerapps.com/e/{environmentId}/s/{slnId}/entity/{entityLogicalName}/form/edit/{formId}");
    }

    public static Uri View(Guid environmentId, string entityLogicalName, Guid viewId, Guid? solutionId = null)
    {
        var slnId = solutionId ?? Guid.Parse(DefaultSolutionId);
        return new($"https://make.powerapps.com/e/{environmentId}/s/{slnId}/entity/{entityLogicalName}/view/{viewId}");
    }

    public static Uri Flow(Guid environmentId, Guid flowId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"https://make.powerautomate.com/environments/{environmentId}/solutions/{solutionId}/flows/{flowId}")
            : new($"https://make.powerautomate.com/environments/{environmentId}/flows/{flowId}");

    public static Uri Bot(Guid environmentId, Guid botId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"https://copilotstudio.microsoft.com/environments/{environmentId}/bots/{botId}?solutionId={solutionId}")
            : new($"https://copilotstudio.microsoft.com/environments/{environmentId}/bots/{botId}");

    public static Uri Dataflow(Guid environmentId, Guid dataflowId)
        => new($"https://make.powerapps.com/environments/{environmentId}/dataintegration/list/{dataflowId}/edit");

    public static Uri SecurityRole(Guid environmentId, Guid roleId, Guid? solutionId = null)
    {
        var slnId = solutionId ?? Guid.Parse(DefaultSolutionId);
        return new($"https://make.powerapps.com/e/{environmentId}/s/{slnId}/securityroles/{roleId}/roleeditor");
    }

    /// <summary>
    /// Fallback for SCF and unrecognized component types — opens the backing entity record form.
    /// </summary>
    public static Uri ScfRecord(string orgUrl, string entityLogicalName, Guid recordId)
        => new($"https://{orgUrl.TrimEnd('/')}/main.aspx?forceUCI=1&newWindow=true&pagetype=entityrecord&etn={entityLogicalName}&id={recordId}");

    // ── Model-driven app runtime URLs ──

    /// <summary>Open a model-driven app by its unique name.</summary>
    public static Uri AppModuleByName(string orgUrl, string uniqueName)
        => new($"https://{NormalizeOrg(orgUrl)}/main.aspx?appname={Uri.EscapeDataString(uniqueName)}");

    /// <summary>Open a model-driven app by its AppModuleId GUID.</summary>
    public static Uri AppModuleById(string orgUrl, Guid appModuleId)
        => new($"https://{NormalizeOrg(orgUrl)}/main.aspx?appid={appModuleId}");

    /// <summary>
    /// Generic deep-link into a model-driven app via <c>main.aspx</c>.
    /// Builds the URL from app identity + pagetype + arbitrary query parameters.
    /// Supports all UCI page types: entityrecord, entitylist, dashboard, webresource,
    /// control, custom, inlinedialog, genux, search, apps.
    /// </summary>
    public static Uri AppModuleDeepLink(string orgUrl, string? appName, Guid? appId, string pageType, IDictionary<string, string> queryParams)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(appName))
            qs.Add($"appname={Uri.EscapeDataString(appName)}");
        else if (appId.HasValue)
            qs.Add($"appid={appId.Value}");

        qs.Add($"pagetype={Uri.EscapeDataString(pageType)}");

        foreach (var (key, value) in queryParams)
            qs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");

        return new Uri($"https://{NormalizeOrg(orgUrl)}/main.aspx?{string.Join("&", qs)}");
    }

    // ── Canvas app runtime URL ──

    /// <summary>
    /// Open a canvas app in the Power Apps player.
    /// Supports screen navigation, custom parameters, and hidden navbar.
    /// </summary>
    public static Uri CanvasApp(Guid environmentId, Guid appId, string? tenantId,
        string? screenName = null, IDictionary<string, string>? customParams = null, bool hideNavbar = false)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(tenantId))
            qs.Add($"tenantId={Uri.EscapeDataString(tenantId)}");
        if (!string.IsNullOrWhiteSpace(screenName))
            qs.Add($"screenName={Uri.EscapeDataString(screenName)}");
        if (hideNavbar)
            qs.Add("hidenavbar=true");
        if (customParams != null)
            foreach (var (key, value) in customParams)
                qs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");

        var query = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return new Uri($"https://apps.powerapps.com/play/e/{environmentId}/a/{appId}{query}");
    }

    // ── Report URL ──

    /// <summary>Open a report in the Dynamics report viewer.</summary>
    public static Uri Report(string orgUrl, Guid reportId, string action = "run")
        => new($"https://{NormalizeOrg(orgUrl)}/crmreports/viewer/viewer.aspx?action={Uri.EscapeDataString(action)}&id=%7b{reportId}%7d");

    // ── Existing Build() for maker portal editor URLs ──

    /// <summary>
    /// Builds the appropriate maker portal editor URL for a component type code.
    /// Returns null if the type requires additional context that wasn't provided.
    /// For app runtime URLs, use <see cref="AppModuleByName"/>, <see cref="AppModuleDeepLink"/>,
    /// <see cref="CanvasApp"/>, or <see cref="Report"/> directly.
    /// </summary>
    public static Uri? Build(
        Guid environmentId,
        string? orgUrl,
        ComponentType typeCode,
        Guid componentId,
        string? entityLogicalName = null,
        Guid? solutionId = null)
    {
        return typeCode switch
        {
            ComponentType.Solution => Solution(environmentId, componentId),
            ComponentType.Entity => Entity(environmentId, componentId, solutionId),
            ComponentType.SystemForm when entityLogicalName != null => Form(environmentId, entityLogicalName, componentId, solutionId),
            ComponentType.Form when entityLogicalName != null => Form(environmentId, entityLogicalName, componentId, solutionId),
            ComponentType.SavedQuery when entityLogicalName != null => View(environmentId, entityLogicalName, componentId, solutionId),
            ComponentType.Workflow => Flow(environmentId, componentId, solutionId),
            ComponentType.Bot => Bot(environmentId, componentId, solutionId),
            ComponentType.Dataflow => Dataflow(environmentId, componentId),
            ComponentType.Role => SecurityRole(environmentId, componentId, solutionId),
            // SCF / unknown — fallback to record form if org URL and entity name available
            _ when orgUrl != null && entityLogicalName != null => ScfRecord(orgUrl, entityLogicalName, componentId),
            _ => null
        };
    }

    private static string NormalizeOrg(string orgUrl)
        => orgUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');
}
