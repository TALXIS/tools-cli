using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionPublishService : ISolutionPublishService
{
    public async Task PublishAsync(
        string? profileName,
        IReadOnlyList<string>? entityLogicalNames,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await SolutionPublisher.PublishAsync(conn.Client, entityLogicalNames, ct).ConfigureAwait(false);
    }
}
