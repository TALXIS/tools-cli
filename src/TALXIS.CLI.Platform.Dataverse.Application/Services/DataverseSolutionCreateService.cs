using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionCreateService : ISolutionCreateService
{
    public async Task<SolutionCreateOutcome> CreateAsync(
        string? profileName,
        SolutionCreateOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionCreator.CreateAsync(conn.Client, options, ct).ConfigureAwait(false);
    }
}
