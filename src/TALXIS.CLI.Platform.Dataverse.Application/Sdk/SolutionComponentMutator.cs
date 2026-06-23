using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Adds and removes components from unmanaged solutions.
/// </summary>
internal static class SolutionComponentMutator
{
    public static async Task AddAsync(
        IOrganizationServiceAsync2 service,
        ComponentAddOptions options,
        CancellationToken ct)
    {
        var request = new AddSolutionComponentRequest
        {
            ComponentId = options.ComponentId,
            ComponentType = options.ComponentType,
            SolutionUniqueName = options.SolutionUniqueName,
            AddRequiredComponents = options.AddRequiredComponents,
            DoNotIncludeSubcomponents = options.DoNotIncludeSubcomponents,
        };

        await service.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    public static async Task RemoveAsync(
        IOrganizationServiceAsync2 service,
        ComponentRemoveOptions options,
        CancellationToken ct)
    {
        var request = new RemoveSolutionComponentRequest
        {
            ComponentId = options.ComponentId,
            ComponentType = options.ComponentType,
            SolutionUniqueName = options.SolutionUniqueName,
        };

        await service.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    public static async Task DeleteFromEnvironmentAsync(
        IOrganizationServiceAsync2 service,
        int componentType,
        Guid objectId,
        CancellationToken ct)
    {
        if (!SolutionComponentEntityMap.TryGetEntityLogicalName(componentType, out var entityLogicalName) || entityLogicalName is null)
        {
            throw new NotSupportedException(
                $"Deleting component type {componentType} from the environment is not supported. " +
                $"Supported component entities: {SolutionComponentEntityMap.SupportedSummary}.");
        }

        await service.DeleteAsync(entityLogicalName, objectId, ct).ConfigureAwait(false);
    }
}
