namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// URL builders for the Power Apps maker portal (<c>make.powerapps.com</c>).
/// Covers solution editor, entity fields, form designer, view designer,
/// security role editor, and dataflow editor.
/// </summary>
public static class MakerPortalUrls
{
    private const string Base = "https://make.powerapps.com";

    public static Uri Solution(Guid environmentId, Guid solutionId)
        => new($"{Base}/environments/{environmentId}/solutions/{solutionId}");

    public static Uri Entity(Guid environmentId, Guid metadataId, Guid? solutionId = null)
        => solutionId.HasValue
            ? new($"{Base}/environments/{environmentId}/solutions/{solutionId}/entities/{metadataId}/fields")
            : new($"{Base}/environments/{environmentId}/entities/{metadataId}/fields");

    public static Uri FormDesigner(Guid environmentId, string entityLogicalName, Guid formId, Guid? solutionId = null)
    {
        var slnId = solutionId ?? Guid.Parse(BrowseUrlConstants.DefaultSolutionId);
        return new($"{Base}/e/{environmentId}/s/{slnId}/entity/{entityLogicalName}/form/edit/{formId}");
    }

    public static Uri ViewDesigner(Guid environmentId, string entityLogicalName, Guid viewId, Guid? solutionId = null)
    {
        var slnId = solutionId ?? Guid.Parse(BrowseUrlConstants.DefaultSolutionId);
        return new($"{Base}/e/{environmentId}/s/{slnId}/entity/{entityLogicalName}/view/{viewId}");
    }

    public static Uri SecurityRoleEditor(Guid environmentId, Guid roleId, Guid? solutionId = null)
    {
        var slnId = solutionId ?? Guid.Parse(BrowseUrlConstants.DefaultSolutionId);
        return new($"{Base}/e/{environmentId}/s/{slnId}/securityroles/{roleId}/roleeditor");
    }

    public static Uri DataflowEditor(Guid environmentId, Guid dataflowId)
        => new($"{Base}/environments/{environmentId}/dataintegration/list/{dataflowId}/edit");
}
