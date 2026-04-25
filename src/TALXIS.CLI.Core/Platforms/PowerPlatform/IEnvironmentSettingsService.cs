namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A single environment setting from any backend (control plane, Organization
/// table, solution settings, copilot governance). The CLI presents all
/// settings as flat key-value pairs regardless of their storage location.
/// </summary>
public sealed record EnvironmentSetting(string Name, object? Value);

/// <summary>
/// Unified service for listing and updating environment settings across
/// all Power Platform backends. Abstracts away the fragmented storage
/// (control plane API, Dataverse Organization table, solution settings,
/// copilot governance) behind a single key-value interface.
/// </summary>
public interface IEnvironmentSettingsService
{
    /// <summary>
    /// Lists environment settings from all backends, merged into a single
    /// flat collection.
    /// </summary>
    Task<IReadOnlyList<EnvironmentSetting>> ListAsync(
        string? profileName,
        CancellationToken ct);

    /// <summary>
    /// Updates a single environment setting. The correct backend is resolved
    /// automatically based on the setting name. The <paramref name="value"/>
    /// string is auto-coerced to the appropriate type (bool / int / string).
    /// </summary>
    Task UpdateAsync(
        string? profileName,
        string settingName,
        string value,
        CancellationToken ct);
}


