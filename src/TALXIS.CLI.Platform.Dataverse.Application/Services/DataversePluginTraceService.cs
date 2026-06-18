using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataversePluginTraceService : IPluginTraceService
{
    public async Task<PluginTraceSetting> GetSettingAsync(string? profileName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginTraceManager.GetSettingAsync(conn.Client, ct).ConfigureAwait(false);
    }

    public async Task<PluginTraceSetting> SetSettingAsync(string? profileName, PluginTraceLevel level, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PluginTraceManager.SetSettingAsync(conn.Client, level, ct).ConfigureAwait(false);
    }
}
