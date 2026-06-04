using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataversePluginInventoryService : IPluginInventoryService
{
    public async Task<IReadOnlyList<PluginAssemblyRecord>> ListAssembliesAsync(
        string? profileName, string? nameContains, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginInventoryManager.ListAssembliesAsync(conn.Client, nameContains, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PluginTypeRecord>> ListTypesAsync(
        string? profileName, string? assemblyContains, PluginKind? kind, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginInventoryManager.ListTypesAsync(conn.Client, assemblyContains, kind, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PluginStepRecord>> ListStepsAsync(
        string? profileName, string? assemblyContains, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginInventoryManager.ListStepsAsync(conn.Client, assemblyContains, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PluginStepImageRecord>> ListStepImagesAsync(
        string? profileName, string? assemblyContains, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginInventoryManager.ListStepImagesAsync(conn.Client, assemblyContains, ct).ConfigureAwait(false);
    }

    public async Task SetStepStateAsync(
        string? profileName, Guid stepId, bool enabled, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await PluginInventoryManager.SetStepStateAsync(conn.Client, stepId, enabled, ct).ConfigureAwait(false);
    }

    public async Task<int> SetStepsStateAsync(
        string? profileName, IReadOnlyCollection<Guid> stepIds, bool enabled, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginInventoryManager.SetStepsStateAsync(conn.Client, stepIds, enabled, ct).ConfigureAwait(false);
    }
}
