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

    /// <summary>
    /// Builds the appropriate URL for a component type code.
    /// Returns null if the type requires additional context that wasn't provided.
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
}
