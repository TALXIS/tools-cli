using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseConnectionReferenceService : IConnectionReferenceService
{
    public async Task<IReadOnlyList<ConnectionReferenceRecord>> ListAsync(
        string? profileName, string? connector, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await ConnectionReferenceReader.ListAsync(conn.Client, connector, ct).ConfigureAwait(false);
    }
}
