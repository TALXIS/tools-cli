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
}
