using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Backend for the Power Platform control plane environment management
/// settings API (<c>api.powerplatform.com/environmentmanagement</c>).
/// Wraps the existing <see cref="EnvironmentSettingsClient"/>.
/// </summary>
internal sealed class ControlPlaneSettingsBackend : ISettingsBackend
{
    private readonly EnvironmentSettingsClient _client;

    public ControlPlaneSettingsBackend(EnvironmentSettingsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        Connection connection, Credential credential, Guid environmentId, CancellationToken ct)
    {
        return await _client.ListAsync(connection, credential, environmentId, selectFilter: null, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryUpdateAsync(
        Connection connection, Credential credential, Guid environmentId,
        string settingName, string value, CancellationToken ct)
    {
        // Check if this setting belongs to the control plane.
        if (!EnvironmentSettingsClient.KnownSettingNames
                .Any(n => n.Equals(settingName, StringComparison.OrdinalIgnoreCase)))
            return false;

        await _client.UpdateAsync(connection, credential, environmentId, settingName, value, ct)
            .ConfigureAwait(false);
        return true;
    }
}
