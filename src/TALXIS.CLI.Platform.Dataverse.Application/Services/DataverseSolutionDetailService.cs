using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="ISolutionDetailService"/>.
/// Connects via profile, delegates to <see cref="SolutionDetailReader"/>.
/// </summary>
internal sealed class DataverseSolutionDetailService : ISolutionDetailService
{
    public async Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> ShowAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionDetailReader.ShowAsync(conn.Client, solutionUniqueName, ct).ConfigureAwait(false);
    }
}
