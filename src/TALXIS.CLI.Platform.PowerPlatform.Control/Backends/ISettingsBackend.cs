using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Internal abstraction for a settings storage backend. Each backend
/// (control plane, Organization table, solution settings, copilot governance)
/// implements this interface. The orchestrator queries all backends and
/// merges results — the CLI consumer never sees this interface.
/// </summary>
internal interface ISettingsBackend
{
    /// <summary>
    /// Retrieves all settings from this backend.
    /// </summary>
    Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        CancellationToken ct);

    /// <summary>
    /// Attempts to update the named setting. Returns <c>true</c> if this
    /// backend owns the setting and the update succeeded; <c>false</c> if
    /// this backend does not recognise the setting name (caller should try
    /// the next backend).
    /// </summary>
    Task<bool> TryUpdateAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        string settingName,
        string value,
        CancellationToken ct);
}
