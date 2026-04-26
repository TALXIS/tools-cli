using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="ISolutionComponentQueryService"/>.
/// Connects via profile, resolves solution ID, delegates to <see cref="SolutionComponentQueryReader"/>.
/// </summary>
internal sealed class DataverseSolutionComponentQueryService : ISolutionComponentQueryService
{
    public async Task<IReadOnlyList<ComponentSummaryRow>> ListAsync(
        string? profileName,
        string solutionUniqueName,
        int? componentTypeFilter,
        string? entityFilter,
        int? top,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var solutionId = await SolutionDetailReader.ResolveIdAsync(conn.Client, solutionUniqueName, ct).ConfigureAwait(false);
        return await SolutionComponentQueryReader.ListAsync(conn.Client, solutionId, componentTypeFilter, entityFilter, top, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ComponentCountRow>> CountAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var solutionId = await SolutionDetailReader.ResolveIdAsync(conn.Client, solutionUniqueName, ct).ConfigureAwait(false);
        return await SolutionComponentQueryReader.CountAsync(conn.Client, solutionId, ct).ConfigureAwait(false);
    }
}
