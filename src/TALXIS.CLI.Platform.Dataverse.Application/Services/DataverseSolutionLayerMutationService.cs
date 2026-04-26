using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionLayerMutationService : ISolutionLayerMutationService
{
    public async Task RemoveCustomizationAsync(
        string? profileName,
        Guid componentId,
        string componentTypeName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // RemoveActiveCustomizationsRequest is not in the SDK proxy DLL at this version,
        // so we construct the OrganizationRequest manually.
        // RemoveActiveCustomizations accepts only ComponentId and SolutionComponentName.
        // ComponentType is not a recognized parameter for this action.
        var request = new OrganizationRequest("RemoveActiveCustomizations")
        {
            ["ComponentId"] = componentId,
            ["SolutionComponentName"] = componentTypeName,
        };

        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
    }
}
