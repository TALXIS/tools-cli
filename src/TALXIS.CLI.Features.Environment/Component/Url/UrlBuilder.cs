using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.Component.Browse;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Shared URL construction logic extracted from the old browse command.
/// Resolves component type, parses parameters, and dispatches to the appropriate URL builder.
/// Reusable from CLI commands, MCP, and tests.
/// </summary>
public static class UrlBuilder
{
    /// <summary>
    /// Builds a URL for a component editor/viewer based on the given type and parameters.
    /// </summary>
    /// <param name="type">Component type string (canonical name, alias, template name, or integer type code).</param>
    /// <param name="parameters">Key-value parameters parsed from --param options.</param>
    /// <param name="profileName">Profile name for connection resolution.</param>
    /// <param name="logger">Logger for error reporting.</param>
    /// <returns>The built URL and resolved type name, or null if building failed.</returns>
    public static async Task<UrlBuilderResult?> BuildUrlAsync(
        string type,
        IReadOnlyDictionary<string, string> parameters,
        string? profileName,
        ILogger logger)
    {
        // Resolve component type
        var def = ComponentDefinitionRegistry.GetByName(type);
        ComponentType? typeCode = def?.TypeCode;
        if (typeCode is null && int.TryParse(type, out var rawCode) && rawCode > 0)
            typeCode = (ComponentType)rawCode;
        if (typeCode is null)
        {
            logger.LogError("Unknown component type '{Type}'. Run 'component type list' to see available types.", type);
            return null;
        }

        // Resolve profile + connection
        var configResolver = TxcServices.Get<IConfigurationResolver>();
        var ctx = await configResolver.ResolveAsync(profileName, CancellationToken.None).ConfigureAwait(false);
        var connection = ctx.Connection;

        if (connection.EnvironmentId is null)
        {
            logger.LogError("Environment ID is not set on the connection. Run 'config connection check' to populate it.");
            return null;
        }
        var environmentId = connection.EnvironmentId.Value;

        // Validate EnvironmentUrl for types that require it (UCI, reports, SCF record forms)
        var needsOrgUrl = typeCode.Value is ComponentType.AppModule or ComponentType.Report
            || (typeCode.Value is not ComponentType.CanvasApp and not ComponentType.Workflow);
        string? orgUrl = null;
        if (!string.IsNullOrWhiteSpace(connection.EnvironmentUrl)
            && Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var orgUri))
        {
            orgUrl = orgUri.Host;
        }
        else if (needsOrgUrl)
        {
            logger.LogError("Environment URL is not set or invalid on the connection. Run 'config connection check' to populate it.");
            return null;
        }

        // Dispatch by component type
        Uri? url = typeCode.Value switch
        {
            ComponentType.AppModule => BuildAppModuleUrl(orgUrl!, parameters, logger),
            ComponentType.CanvasApp => BuildCanvasAppUrl(environmentId, connection.TenantId, parameters, logger),
            ComponentType.Report => BuildReportUrl(orgUrl!, parameters, logger),
            ComponentType.Workflow => await BuildFlowUrlAsync(environmentId, parameters, profileName, logger).ConfigureAwait(false),
            _ => await BuildMakerEditorUrlAsync(typeCode.Value, environmentId, orgUrl, parameters, profileName, logger).ConfigureAwait(false)
        };

        if (url is null)
            return null;

        return new UrlBuilderResult(url, def?.Name ?? typeCode.Value.ToString());
    }

    /// <summary>
    /// Parses a list of "key=value" strings into a dictionary.
    /// </summary>
    public static Dictionary<string, string> ParseParams(IEnumerable<string> paramStrings)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paramStrings)
        {
            var idx = p.IndexOf('=');
            if (idx > 0 && idx < p.Length - 1)
                dict[p[..idx]] = p[(idx + 1)..];
        }
        return dict;
    }

    // ── Helper: read optional param from dictionary ──

    private static string? Get(IReadOnlyDictionary<string, string> p, string key)
        => p.TryGetValue(key, out var v) ? v : null;

    // ── AppModule URL builder ──

    private static Uri? BuildAppModuleUrl(string orgUrl, IReadOnlyDictionary<string, string> p, ILogger logger)
    {
        var pageType = Get(p, "pagetype");
        var name = Get(p, "name");
        var id = Get(p, "id");

        if (string.IsNullOrWhiteSpace(pageType))
        {
            if (!string.IsNullOrWhiteSpace(name))
                return PowerAppsUciUrls.AppByName(orgUrl, name);
            if (!string.IsNullOrWhiteSpace(id) && Guid.TryParse(id, out var appId))
                return PowerAppsUciUrls.AppById(orgUrl, appId);
            logger.LogError("Provide 'name' or 'id' parameter for the app module.");
            return null;
        }

        var queryParams = new Dictionary<string, string>();
        void AddIf(string paramKey, string qsKey) { var v = Get(p, paramKey); if (!string.IsNullOrWhiteSpace(v)) queryParams[qsKey] = v; }

        AddIf("entity", "etn");
        AddIf("record", "id");
        AddIf("formid", "formid");
        if (!string.IsNullOrWhiteSpace(Get(p, "viewid")))
        {
            queryParams["viewid"] = Get(p, "viewid")!;
            queryParams["viewtype"] = "1039";
        }
        AddIf("dashboard", "id");
        AddIf("custom-page", "name");
        AddIf("control", "controlName");
        AddIf("webresource", "webresourceName");
        AddIf("genux", "id");
        AddIf("dialog-name", "name");
        AddIf("dialog-options", "dialogOptions");
        AddIf("data", "data");
        AddIf("extraqs", "extraqs");
        AddIf("navbar", "navbar");
        AddIf("cmdbar", "cmdbar");

        Guid? appId2 = null;
        if (!string.IsNullOrWhiteSpace(id) && Guid.TryParse(id, out var parsed))
            appId2 = parsed;

        return PowerAppsUciUrls.DeepLink(orgUrl, name, appId2, pageType, queryParams);
    }

    // ── Flow URL builder ──

    private static async Task<Uri?> BuildFlowUrlAsync(
        Guid environmentId, IReadOnlyDictionary<string, string> p, string? profileName, ILogger logger)
    {
        var id = Get(p, "id");
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var flowId))
        {
            logger.LogError("'id' parameter (GUID) is required for flows.");
            return null;
        }

        Guid? solutionId = await ResolveSolutionIdAsync(Get(p, "solution"), profileName).ConfigureAwait(false);

        var run = Get(p, "run");
        if (!string.IsNullOrWhiteSpace(run))
            return PowerAutomateUrls.FlowRun(environmentId, flowId, run, solutionId);

        var flowView = Get(p, "flow-view");
        return (flowView?.ToLowerInvariant()) switch
        {
            "details" => PowerAutomateUrls.FlowDetails(environmentId, flowId, solutionId),
            "runs" => PowerAutomateUrls.FlowRuns(environmentId, flowId, solutionId),
            _ => PowerAutomateUrls.FlowEditor(environmentId, flowId, solutionId)
        };
    }

    // ── Canvas app URL builder ──

    private static Uri? BuildCanvasAppUrl(
        Guid environmentId, string? tenantId, IReadOnlyDictionary<string, string> p, ILogger logger)
    {
        var id = Get(p, "id");
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var appId))
        {
            logger.LogError("'id' parameter (GUID) is required for canvas apps.");
            return null;
        }

        var screen = Get(p, "screen");
        var hideNavbar = string.Equals(Get(p, "hidenavbar"), "true", StringComparison.OrdinalIgnoreCase);

        // Collect remaining params as custom canvas app parameters
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "screen", "hidenavbar" };
        var customParams = new Dictionary<string, string>();
        foreach (var kvp in p)
        {
            if (!reserved.Contains(kvp.Key))
                customParams[kvp.Key] = kvp.Value;
        }

        return CanvasAppUrls.Play(environmentId, appId, tenantId, screen,
            customParams.Count > 0 ? customParams : null, hideNavbar);
    }

    // ── Report URL builder ──

    private static Uri? BuildReportUrl(string orgUrl, IReadOnlyDictionary<string, string> p, ILogger logger)
    {
        var id = Get(p, "id");
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var reportId))
        {
            logger.LogError("'id' parameter (GUID) is required for reports.");
            return null;
        }
        var action = Get(p, "report-action") ?? "run";
        return PowerAppsUciUrls.Report(orgUrl, reportId, action);
    }

    // ── Maker portal editor URL builder (generic types) ──

    private static async Task<Uri?> BuildMakerEditorUrlAsync(
        ComponentType typeCode, Guid environmentId, string? orgUrl,
        IReadOnlyDictionary<string, string> p, string? profileName, ILogger logger)
    {
        var id = Get(p, "id");
        var name = Get(p, "name");
        var entity = Get(p, "entity");

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
        {
            logger.LogError("Provide 'id' or 'name' parameter.");
            return null;
        }
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
        {
            logger.LogError("'id' and 'name' are mutually exclusive.");
            return null;
        }

        Guid componentId;
        if (!string.IsNullOrWhiteSpace(id))
        {
            if (!Guid.TryParse(id, out componentId))
            {
                logger.LogError("Invalid GUID: '{Id}'.", id);
                return null;
            }
        }
        else
        {
            var resolved = await ResolveNameToGuidAsync(typeCode, name!, profileName, logger).ConfigureAwait(false);
            if (resolved is null) return null;
            componentId = resolved.Value;
        }

        Guid? solutionId = await ResolveSolutionIdAsync(Get(p, "solution"), profileName).ConfigureAwait(false);

        if (typeCode is ComponentType.SystemForm or ComponentType.Form or ComponentType.SavedQuery
            && string.IsNullOrWhiteSpace(entity))
        {
            logger.LogError("'entity' parameter is required for form/view types.");
            return null;
        }

        var url = BuildEditorUrl(typeCode, environmentId, orgUrl, componentId, entity, solutionId);
        if (url is null)
            logger.LogError("Cannot build URL for type code {Code}. For SCF types, provide 'entity' parameter.", (int)typeCode);
        return url;
    }

    /// <summary>Dispatches to the appropriate URL builder based on component type.</summary>
    private static Uri? BuildEditorUrl(ComponentType typeCode, Guid envId, string? orgUrl, Guid componentId, string? entity, Guid? solutionId)
    {
        return typeCode switch
        {
            ComponentType.Solution => MakerPortalUrls.Solution(envId, componentId),
            ComponentType.Entity => MakerPortalUrls.Entity(envId, componentId, solutionId),
            ComponentType.SystemForm when entity != null => MakerPortalUrls.FormDesigner(envId, entity, componentId, solutionId),
            ComponentType.Form when entity != null => MakerPortalUrls.FormDesigner(envId, entity, componentId, solutionId),
            ComponentType.SavedQuery when entity != null => MakerPortalUrls.ViewDesigner(envId, entity, componentId, solutionId),
            ComponentType.Bot => CopilotStudioUrls.BotEditor(envId, componentId, solutionId),
            ComponentType.Dataflow => MakerPortalUrls.DataflowEditor(envId, componentId),
            ComponentType.Role => MakerPortalUrls.SecurityRoleEditor(envId, componentId, solutionId),
            // SCF / unknown — fallback to UCI record form
            _ when orgUrl != null && entity != null => PowerAppsUciUrls.RecordForm(orgUrl, entity, componentId),
            _ => null
        };
    }

    // ── Name-to-GUID resolution ──

    private static async Task<Guid?> ResolveNameToGuidAsync(ComponentType typeCode, string name, string? profileName, ILogger logger)
    {
        switch (typeCode)
        {
            case ComponentType.Solution:
                var slnService = TxcServices.Get<ISolutionDetailService>();
                var (sln, _) = await slnService.ShowAsync(profileName, name, CancellationToken.None).ConfigureAwait(false);
                return sln.Id;

            case ComponentType.Entity:
                var metadataResolver = TxcServices.Get<IMetadataIdResolver>();
                return await metadataResolver.ResolveEntityIdAsync(profileName, name, CancellationToken.None).ConfigureAwait(false);

            case ComponentType.AppModule:
                logger.LogError("AppModule name resolution is handled via 'name' parameter directly.");
                return null;

            default:
                logger.LogError("'name' parameter is not supported for this type. Use 'id' parameter instead.");
                return null;
        }
    }

    /// <summary>Resolves a solution unique name to its GUID, if provided.</summary>
    private static async Task<Guid?> ResolveSolutionIdAsync(string? solutionUniqueName, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(solutionUniqueName))
            return null;
        var slnService = TxcServices.Get<ISolutionDetailService>();
        var (sln, _) = await slnService.ShowAsync(profileName, solutionUniqueName, CancellationToken.None).ConfigureAwait(false);
        return sln.Id;
    }
}

/// <summary>Result of a successful URL build operation.</summary>
public sealed record UrlBuilderResult(Uri Url, string TypeName);
