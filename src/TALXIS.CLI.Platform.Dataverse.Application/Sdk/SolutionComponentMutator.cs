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
}
