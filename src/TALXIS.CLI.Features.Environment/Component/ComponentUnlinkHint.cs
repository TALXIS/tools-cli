using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Component;

/// <summary>
/// Builds ready-to-run commands that remove N:N links blocking a component delete.
/// </summary>
public static class ComponentUnlinkHint
{
    private const int RoleType = 20;
    private const int AppModuleType = 80;

    /// <summary>
    /// Returns one disassociate command per blocker that is only an N:N link, not a real dependent.
    /// </summary>
    public static IReadOnlyList<string> Build(int componentType, Guid componentId, IReadOnlyList<DependencyRow> blockers)
    {
        // A role assigned to a model-driven app is held by appmoduleroles_association, which
        // importing a solution without the role does not remove.
        if (componentType != RoleType) return [];

        return blockers
            .Where(b => b.DependentComponentType == AppModuleType)
            .Select(b => b.DependentComponentId)
            .Distinct()
            .Select(appId =>
                $"txc env data record disassociate {appId} --entity appmodule --target {componentId} " +
                "--target-entity role --relationship appmoduleroles_association --apply --yes")
            .ToList();
    }
}
