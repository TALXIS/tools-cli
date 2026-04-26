using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="ISolutionDependencyService"/>.
/// Connects via profile, delegates to <see cref="SolutionDependencyReader"/>.
/// </summary>
internal sealed class DataverseSolutionDependencyService : ISolutionDependencyService
{
    public async Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionDependencyReader.CheckUninstallAsync(conn.Client, solutionUniqueName, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionDependencyReader.GetDependentsAsync(conn.Client, componentId, componentType, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionDependencyReader.GetRequiredAsync(conn.Client, componentId, componentType, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionDependencyReader.CheckDeleteAsync(conn.Client, componentId, componentType, ct).ConfigureAwait(false);
    }
}
