using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionComponentMutationService : ISolutionComponentMutationService
{
    public async Task AddAsync(string? profileName, ComponentAddOptions options, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await SolutionComponentMutator.AddAsync(conn.Client, options, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string? profileName, ComponentRemoveOptions options, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await SolutionComponentMutator.RemoveAsync(conn.Client, options, ct).ConfigureAwait(false);
    }
}
