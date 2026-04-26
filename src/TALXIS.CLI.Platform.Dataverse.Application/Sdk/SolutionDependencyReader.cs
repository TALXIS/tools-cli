using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reads solution dependency information via SDK typed messages.
/// </summary>
internal static class SolutionDependencyReader
{
    /// <summary>
    /// Retrieves all dependencies that would block uninstalling the solution.
    /// Uses <see cref="RetrieveDependenciesForUninstallRequest"/> which takes
    /// a solution unique name directly.
    /// </summary>
    public static async Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        IOrganizationServiceAsync2 service,
        string solutionUniqueName,
        CancellationToken ct)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (string.IsNullOrWhiteSpace(solutionUniqueName))
            throw new ArgumentException("Solution unique name is required.", nameof(solutionUniqueName));

        var request = new RetrieveDependenciesForUninstallRequest
        {
            SolutionUniqueName = solutionUniqueName,
        };

        var response = (RetrieveDependenciesForUninstallResponse)
            await service.ExecuteAsync(request, ct).ConfigureAwait(false);

        return ParseDependencies(response.EntityCollection);
    }

    internal static IReadOnlyList<DependencyRow> ParseDependencies(EntityCollection collection)
    {
        var rows = new List<DependencyRow>(collection.Entities.Count);
        foreach (var entity in collection.Entities)
        {
            rows.Add(new DependencyRow(
                DependencyId: entity.Id,
                DependentComponentId: entity.GetAttributeValue<Guid>("dependentcomponentobjectid"),
                DependentComponentType: GetOptionSetInt(entity, "dependentcomponenttype"),
                DependentBaseSolutionId: entity.GetAttributeValue<Guid>("dependentcomponentbasesolutionid"),
                RequiredComponentId: entity.GetAttributeValue<Guid>("requiredcomponentobjectid"),
                RequiredComponentType: GetOptionSetInt(entity, "requiredcomponenttype"),
                RequiredBaseSolutionId: entity.GetAttributeValue<Guid>("requiredcomponentbasesolutionid"),
                DependencyType: GetOptionSetInt(entity, "dependencytype")));
        }

        return rows;
    }

    /// <summary>
    /// Returns components that depend on the specified component.
    /// </summary>
    public static async Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(
        IOrganizationServiceAsync2 service,
        Guid componentId,
        int componentType,
        CancellationToken ct)
    {
        var request = new RetrieveDependentComponentsRequest
        {
            ObjectId = componentId,
            ComponentType = componentType,
        };
        var response = (RetrieveDependentComponentsResponse)
            await service.ExecuteAsync(request, ct).ConfigureAwait(false);
        return ParseDependencies(response.EntityCollection);
    }

    /// <summary>
    /// Returns components that the specified component requires.
    /// </summary>
    public static async Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(
        IOrganizationServiceAsync2 service,
        Guid componentId,
        int componentType,
        CancellationToken ct)
    {
        var request = new RetrieveRequiredComponentsRequest
        {
            ObjectId = componentId,
            ComponentType = componentType,
        };
        var response = (RetrieveRequiredComponentsResponse)
            await service.ExecuteAsync(request, ct).ConfigureAwait(false);
        return ParseDependencies(response.EntityCollection);
    }

    /// <summary>
    /// Returns dependencies that would block deletion of the specified component.
    /// </summary>
    public static async Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(
        IOrganizationServiceAsync2 service,
        Guid componentId,
        int componentType,
        CancellationToken ct)
    {
        var request = new RetrieveDependenciesForDeleteRequest
        {
            ObjectId = componentId,
            ComponentType = componentType,
        };
        var response = (RetrieveDependenciesForDeleteResponse)
            await service.ExecuteAsync(request, ct).ConfigureAwait(false);
        return ParseDependencies(response.EntityCollection);
    }

    /// <summary>
    /// Gets an integer value from an entity attribute that may be stored as
    /// <see cref="OptionSetValue"/> or raw <see cref="int"/>.
    /// </summary>
    private static int GetOptionSetInt(Entity entity, string attributeName)
    {
        var raw = entity.Attributes.TryGetValue(attributeName, out var val) ? val : null;
        return raw switch
        {
            OptionSetValue osv => osv.Value,
            int i => i,
            _ => 0,
        };
    }
}
