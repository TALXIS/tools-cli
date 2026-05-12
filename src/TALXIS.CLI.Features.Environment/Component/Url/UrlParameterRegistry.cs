namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Static registry declaring what URL parameters each component type accepts.
/// Used by <see cref="UrlParameterListCliCommand"/> to display available parameters
/// and by <see cref="UrlBuilder"/> to validate input.
/// </summary>
public static class UrlParameterRegistry
{
    /// <summary>Describes a single URL parameter for a component type.</summary>
    public record UrlParameter(string Name, string Description, bool Required = false, string? DefaultValue = null);

    /// <summary>Parameters shared across most maker-portal component types.</summary>
    private static readonly UrlParameter[] CommonParams =
    {
        new("id", "Component GUID. Mutually exclusive with 'name'."),
        new("name", "Component friendly name (resolved to GUID). Mutually exclusive with 'id'. Supported for: solution (unique name), entity (logical name)."),
        new("solution", "Solution unique name for solution-scoped URLs. Resolved to GUID."),
    };

    /// <summary>Per-type parameter definitions.</summary>
    private static readonly Dictionary<string, UrlParameter[]> TypeParams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Solution"] = new[]
        {
            new UrlParameter("id", "Solution GUID."),
            new UrlParameter("name", "Solution unique name (resolved to GUID)."),
        },
        ["Entity"] = new[]
        {
            new UrlParameter("id", "Entity MetadataId GUID."),
            new UrlParameter("name", "Entity logical name (resolved to MetadataId)."),
            new UrlParameter("solution", "Solution context (unique name, resolved to GUID)."),
        },
        ["SystemForm"] = new[]
        {
            new UrlParameter("id", "Form GUID.", Required: true),
            new UrlParameter("entity", "Entity logical name.", Required: true),
            new UrlParameter("solution", "Solution context (unique name, resolved to GUID)."),
        },
        ["SavedQuery"] = new[]
        {
            new UrlParameter("id", "View GUID.", Required: true),
            new UrlParameter("entity", "Entity logical name.", Required: true),
            new UrlParameter("solution", "Solution context (unique name, resolved to GUID)."),
        },
        ["Bot"] = new[]
        {
            new UrlParameter("id", "Bot/agent GUID.", Required: true),
            new UrlParameter("solution", "Solution context (unique name, resolved to GUID)."),
        },
        ["Dataflow"] = new[]
        {
            new UrlParameter("id", "Dataflow GUID.", Required: true),
        },
        ["Role"] = new[]
        {
            new UrlParameter("id", "Security role GUID.", Required: true),
            new UrlParameter("solution", "Solution context (unique name, resolved to GUID)."),
        },
        ["AppModule"] = new[]
        {
            new UrlParameter("name", "App unique name."),
            new UrlParameter("id", "App GUID."),
            new UrlParameter("pagetype", "UCI page type: entityrecord, entitylist, dashboard, webresource, control, custom, inlinedialog, genux, search."),
            new UrlParameter("entity", "Entity logical name (for entityrecord/entitylist page types)."),
            new UrlParameter("record", "Record GUID to open in an entity form (pagetype=entityrecord)."),
            new UrlParameter("formid", "Specific form GUID to use for entity record."),
            new UrlParameter("viewid", "View GUID for entity list (pagetype=entitylist)."),
            new UrlParameter("dashboard", "Dashboard GUID (pagetype=dashboard)."),
            new UrlParameter("custom-page", "Custom page logical name (pagetype=custom)."),
            new UrlParameter("control", "Full-page PCF control fully-qualified name (pagetype=control)."),
            new UrlParameter("webresource", "Web resource logical name (pagetype=webresource)."),
            new UrlParameter("genux", "Generative/Copilot AI page ID (pagetype=genux)."),
            new UrlParameter("dialog-name", "Inline dialog name (pagetype=inlinedialog)."),
            new UrlParameter("dialog-options", "Inline dialog options JSON (pagetype=inlinedialog)."),
            new UrlParameter("data", "Data parameter for control, genux, or webresource page types."),
            new UrlParameter("extraqs", "Pre-populate form fields as key=value pairs (URL-encoded automatically)."),
            new UrlParameter("navbar", "Navigation bar mode: on, off, entity."),
            new UrlParameter("cmdbar", "Show command bar: true or false."),
        },
        ["CanvasApp"] = new[]
        {
            new UrlParameter("id", "Canvas app GUID.", Required: true),
            new UrlParameter("screen", "Canvas app screen name to navigate to on launch."),
            new UrlParameter("hidenavbar", "Hide the Power Apps navigation bar (true/false).", DefaultValue: "false"),
        },
        ["Workflow"] = new[]
        {
            new UrlParameter("id", "Flow GUID.", Required: true),
            new UrlParameter("solution", "Solution context (unique name, resolved to GUID)."),
            new UrlParameter("flow-view", "Page to open: editor (default), details, or runs.", DefaultValue: "editor"),
            new UrlParameter("run", "Specific flow run ID to open."),
        },
        ["Report"] = new[]
        {
            new UrlParameter("id", "Report GUID.", Required: true),
            new UrlParameter("report-action", "Report viewer action: run (default) or filter.", DefaultValue: "run"),
        },
    };

    /// <summary>
    /// Returns the URL parameters accepted by the given component type.
    /// Falls back to <see cref="CommonParams"/> for types without explicit definitions.
    /// </summary>
    public static IReadOnlyList<UrlParameter> GetParameters(string componentTypeName)
    {
        return TypeParams.TryGetValue(componentTypeName, out var typeSpecific)
            ? typeSpecific
            : CommonParams;
    }

    /// <summary>Returns all registered component type names that have parameter definitions.</summary>
    public static IReadOnlyList<string> GetRegisteredTypes() => TypeParams.Keys.OrderBy(k => k).ToList();
}
