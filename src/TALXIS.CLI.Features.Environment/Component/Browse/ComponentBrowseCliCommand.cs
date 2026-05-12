using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// Opens the Power Platform web editor for a component instance.
/// Resolves the appropriate URL based on component type and opens it in the default browser.
/// In headless mode, only prints the URL without opening the browser.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "browse",
    Description = "Open a component editor, app, record, view, or report in the browser for the connected live environment. Returns the URL. Requires an active profile."
)]
public class ComponentBrowseCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<ComponentBrowseCliCommand>();

    [CliOption(Name = "--type", Description = "Component type (name, alias, or integer code). Run 'txc component type list' to see available types.", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--id", Description = "Component GUID. Mutually exclusive with --name.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--name", Description = "Component friendly name (resolved to GUID). Mutually exclusive with --id. Supported for: solution (unique name), entity (logical name).", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name. Required for form/view types. Also provides the backing entity name for SCF types.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--solution", Description = "Solution unique name for solution-scoped URLs. Resolved to GUID.", Required = false)]
    public string? Solution { get; set; }

    // ── App deep-link options (model-driven apps) ──

    [CliOption(Name = "--pagetype", Description = "UCI page type for deep-linking within an app: entityrecord, entitylist, dashboard, webresource, control, custom, inlinedialog, genux, search.", Required = false)]
    public string? PageType { get; set; }

    [CliOption(Name = "--record", Description = "Record GUID to open in an entity form (pagetype=entityrecord).", Required = false)]
    public string? Record { get; set; }

    [CliOption(Name = "--formid", Description = "Specific form GUID to use for entity record.", Required = false)]
    public string? FormId { get; set; }

    [CliOption(Name = "--viewid", Description = "View GUID for entity list (pagetype=entitylist).", Required = false)]
    public string? ViewId { get; set; }

    [CliOption(Name = "--dashboard", Description = "Dashboard GUID (pagetype=dashboard).", Required = false)]
    public string? Dashboard { get; set; }

    [CliOption(Name = "--custom-page", Description = "Custom page logical name (pagetype=custom).", Required = false)]
    public string? CustomPage { get; set; }

    [CliOption(Name = "--control", Description = "Full-page PCF control fully-qualified name (pagetype=control).", Required = false)]
    public string? Control { get; set; }

    [CliOption(Name = "--webresource", Description = "Web resource logical name (pagetype=webresource).", Required = false)]
    public string? WebResource { get; set; }

    [CliOption(Name = "--genux", Description = "Generative/Copilot AI page ID (pagetype=genux).", Required = false)]
    public string? Genux { get; set; }

    [CliOption(Name = "--dialog-name", Description = "Inline dialog name (pagetype=inlinedialog).", Required = false)]
    public string? DialogName { get; set; }

    [CliOption(Name = "--dialog-options", Description = "Inline dialog options JSON (pagetype=inlinedialog).", Required = false)]
    public string? DialogOptions { get; set; }

    [CliOption(Name = "--data", Description = "Data parameter for control, genux, or webresource page types.", Required = false)]
    public string? Data { get; set; }

    [CliOption(Name = "--extraqs", Description = "Pre-populate form fields as key=value pairs (URL-encoded automatically).", Required = false)]
    public string? ExtraQs { get; set; }

    [CliOption(Name = "--navbar", Description = "Navigation bar mode: on, off, entity.", Required = false)]
    public string? Navbar { get; set; }

    [CliOption(Name = "--cmdbar", Description = "Show command bar: true or false.", Required = false)]
    public string? Cmdbar { get; set; }

    // ── Canvas app options ──

    [CliOption(Name = "--screen", Description = "Canvas app screen name to navigate to on launch.", Required = false)]
    public string? Screen { get; set; }

    [CliOption(Name = "--param", Description = "Canvas app custom parameter (key=value). Can be specified multiple times.", Required = false)]
    public List<string> Param { get; set; } = new();

    [CliOption(Name = "--hidenavbar", Description = "Canvas app: hide the Power Apps navigation bar.", Required = false)]
    public bool HideNavbar { get; set; }

    // ── Power Automate options ──

    [CliOption(Name = "--flow-view", Description = "Flow page to open: editor (default), details, or runs.", Required = false)]
    public string? FlowView { get; set; }

    [CliOption(Name = "--run", Description = "Specific flow run ID to open.", Required = false)]
    public string? Run { get; set; }

    // ── Report options ──

    [CliOption(Name = "--report-action", Description = "Report viewer action: run (default) or filter.", Required = false)]
    public string? ReportAction { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        // Resolve component type
        var def = ComponentDefinitionRegistry.GetByName(Type);
        ComponentType? typeCode = def?.TypeCode;
        if (typeCode is null && int.TryParse(Type, out var rawCode) && rawCode > 0)
            typeCode = (ComponentType)rawCode;
        if (typeCode is null)
        {
            Logger.LogError("Unknown component type '{Type}'. Run 'txc component type list' to see available types.", Type);
            return ExitValidationError;
        }

        // Resolve profile + connection
        var configResolver = TxcServices.Get<IConfigurationResolver>();
        var ctx = await configResolver.ResolveAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var connection = ctx.Connection;

        if (connection.EnvironmentId is null)
        {
            Logger.LogError("Environment ID is not set on the connection. Run 'txc config connection check' to populate it.");
            return ExitValidationError;
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
            Logger.LogError("Environment URL is not set or invalid on the connection. Run 'txc config connection check' to populate it.");
            return ExitValidationError;
        }

        // Dispatch by component type
        Uri? url = typeCode.Value switch
        {
            ComponentType.AppModule => BuildAppModuleUrl(orgUrl!),
            ComponentType.CanvasApp => BuildCanvasAppUrl(environmentId, connection.TenantId),
            ComponentType.Report => BuildReportUrl(orgUrl!),
            ComponentType.Workflow => await BuildFlowUrlAsync(environmentId).ConfigureAwait(false),
            _ => await BuildMakerEditorUrlAsync(typeCode.Value, environmentId, orgUrl).ConfigureAwait(false)
        };

        if (url is null)
            return ExitValidationError; // error already logged

        OutputFormatter.WriteData(new { url = url.AbsoluteUri, type = def?.Name ?? typeCode.Value.ToString() },
            _ => OutputWriter.WriteLine(url.AbsoluteUri));

        BrowserLauncher.Open(url, Logger);
        return ExitSuccess;
    }

    /// <summary>Build URL for model-driven app (shell or deep-link via pagetype).</summary>
    private Uri? BuildAppModuleUrl(string orgUrl)
    {
        if (string.IsNullOrWhiteSpace(PageType))
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return PowerAppsUciUrls.AppByName(orgUrl, Name);
            if (!string.IsNullOrWhiteSpace(Id) && Guid.TryParse(Id, out var appId))
                return PowerAppsUciUrls.AppById(orgUrl, appId);
            Logger.LogError("Provide --name <uniqueName> or --id <guid> for the app module.");
            return null;
        }

        var queryParams = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(Entity)) queryParams["etn"] = Entity;
        if (!string.IsNullOrWhiteSpace(Record)) queryParams["id"] = Record;
        if (!string.IsNullOrWhiteSpace(FormId)) queryParams["formid"] = FormId;
        if (!string.IsNullOrWhiteSpace(ViewId)) { queryParams["viewid"] = ViewId; queryParams["viewtype"] = "1039"; }
        if (!string.IsNullOrWhiteSpace(Dashboard)) queryParams["id"] = Dashboard;
        if (!string.IsNullOrWhiteSpace(CustomPage)) queryParams["name"] = CustomPage;
        if (!string.IsNullOrWhiteSpace(Control)) queryParams["controlName"] = Control;
        if (!string.IsNullOrWhiteSpace(WebResource)) queryParams["webresourceName"] = WebResource;
        if (!string.IsNullOrWhiteSpace(Genux)) queryParams["id"] = Genux;
        if (!string.IsNullOrWhiteSpace(DialogName)) queryParams["name"] = DialogName;
        if (!string.IsNullOrWhiteSpace(DialogOptions)) queryParams["dialogOptions"] = DialogOptions;
        if (!string.IsNullOrWhiteSpace(Data)) queryParams["data"] = Data;
        if (!string.IsNullOrWhiteSpace(ExtraQs)) queryParams["extraqs"] = ExtraQs;
        if (!string.IsNullOrWhiteSpace(Navbar)) queryParams["navbar"] = Navbar;
        if (!string.IsNullOrWhiteSpace(Cmdbar)) queryParams["cmdbar"] = Cmdbar;

        Guid? appId2 = null;
        if (!string.IsNullOrWhiteSpace(Id) && Guid.TryParse(Id, out var parsed))
            appId2 = parsed;

        return PowerAppsUciUrls.DeepLink(orgUrl, Name, appId2, PageType, queryParams);
    }

    /// <summary>Build URL for Power Automate flow — editor, details, runs, or specific run.</summary>
    private async Task<Uri?> BuildFlowUrlAsync(Guid environmentId)
    {
        if (string.IsNullOrWhiteSpace(Id) || !Guid.TryParse(Id, out var flowId))
        {
            Logger.LogError("--id <guid> is required for flows.");
            return null;
        }

        Guid? solutionId = null;
        if (!string.IsNullOrWhiteSpace(Solution))
        {
            var slnService = TxcServices.Get<ISolutionDetailService>();
            var (sln, _) = await slnService.ShowAsync(Profile, Solution, CancellationToken.None).ConfigureAwait(false);
            solutionId = sln.Id;
        }

        if (!string.IsNullOrWhiteSpace(Run))
            return PowerAutomateUrls.FlowRun(environmentId, flowId, Run, solutionId);

        return (FlowView?.ToLowerInvariant()) switch
        {
            "details" => PowerAutomateUrls.FlowDetails(environmentId, flowId, solutionId),
            "runs" => PowerAutomateUrls.FlowRuns(environmentId, flowId, solutionId),
            _ => PowerAutomateUrls.FlowEditor(environmentId, flowId, solutionId)
        };
    }

    /// <summary>Build URL for canvas app player.</summary>
    private Uri? BuildCanvasAppUrl(Guid environmentId, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(Id) || !Guid.TryParse(Id, out var appId))
        {
            Logger.LogError("--id <guid> is required for canvas apps.");
            return null;
        }

        var customParams = new Dictionary<string, string>();
        foreach (var p in Param)
        {
            var idx = p.IndexOf('=');
            if (idx > 0 && idx < p.Length - 1)
                customParams[p[..idx]] = p[(idx + 1)..];
        }

        return CanvasAppUrls.Play(environmentId, appId, tenantId, Screen,
            customParams.Count > 0 ? customParams : null, HideNavbar);
    }

    /// <summary>Build URL for report viewer.</summary>
    private Uri? BuildReportUrl(string orgUrl)
    {
        if (string.IsNullOrWhiteSpace(Id) || !Guid.TryParse(Id, out var reportId))
        {
            Logger.LogError("--id <guid> is required for reports.");
            return null;
        }
        return PowerAppsUciUrls.Report(orgUrl, reportId, ReportAction ?? "run");
    }

    /// <summary>Build URL for maker portal editor (existing component types).</summary>
    private async Task<Uri?> BuildMakerEditorUrlAsync(ComponentType typeCode, Guid environmentId, string? orgUrl)
    {
        // Validate --id / --name
        if (string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("Provide --id <guid> or --name <friendly-name>.");
            return null;
        }
        if (!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("--id and --name are mutually exclusive.");
            return null;
        }

        Guid componentId;
        if (!string.IsNullOrWhiteSpace(Id))
        {
            if (!Guid.TryParse(Id, out componentId))
            {
                Logger.LogError("Invalid GUID: '{Id}'.", Id);
                return null;
            }
        }
        else
        {
            var resolved = await ResolveNameToGuidAsync(typeCode, Name!).ConfigureAwait(false);
            if (resolved is null) return null;
            componentId = resolved.Value;
        }

        // Resolve --solution
        Guid? solutionId = null;
        if (!string.IsNullOrWhiteSpace(Solution))
        {
            var slnService = TxcServices.Get<ISolutionDetailService>();
            var (sln, _) = await slnService.ShowAsync(Profile, Solution, CancellationToken.None).ConfigureAwait(false);
            solutionId = sln.Id;
        }

        if (typeCode is ComponentType.SystemForm or ComponentType.Form or ComponentType.SavedQuery
            && string.IsNullOrWhiteSpace(Entity))
        {
            Logger.LogError("--entity is required for form/view types.");
            return null;
        }

        var url = BuildEditorUrl(typeCode, environmentId, orgUrl, componentId, Entity, solutionId);
        if (url is null)
            Logger.LogError("Cannot build URL for type '{Type}' (code {Code}). For SCF types, provide --entity.", Type, (int)typeCode);
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

    private async Task<Guid?> ResolveNameToGuidAsync(ComponentType typeCode, string name)
    {
        switch (typeCode)
        {
            case ComponentType.Solution:
                var slnService = TxcServices.Get<ISolutionDetailService>();
                var (sln, _) = await slnService.ShowAsync(Profile, name, CancellationToken.None).ConfigureAwait(false);
                return sln.Id;

            case ComponentType.Entity:
                var metadataResolver = TxcServices.Get<IMetadataIdResolver>();
                return await metadataResolver.ResolveEntityIdAsync(Profile, name, CancellationToken.None).ConfigureAwait(false);

            case ComponentType.AppModule:
                // AppModule name resolution handled in BuildAppModuleUrl
                Logger.LogError("AppModule name resolution is handled via --name directly.");
                return null;

            default:
                Logger.LogError("--name is not supported for type '{Type}'. Use --id <guid> instead.", Type);
                return null;
        }
    }
}
