namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A single environment management setting returned by the Power Platform
/// control plane API (<c>api.powerplatform.com/environmentmanagement</c>).
/// </summary>
public sealed record EnvironmentManagementSetting(string Name, object? Value);

/// <summary>
/// Lists and updates environment management settings via the Power Platform
/// control plane API. These are governance/feature-toggle settings
/// (e.g. <c>powerApps_AllowCodeApps</c>, Copilot Studio flags, SAS IP
/// restrictions) — distinct from the Dataverse Organization table columns
/// that <c>pac env list-settings</c> surfaces.
/// </summary>
public interface IEnvironmentManagementSettingsService
{
    /// <summary>
    /// Lists environment management settings. When <paramref name="selectFilter"/>
    /// is provided it is passed as the <c>$select</c> OData query parameter to
    /// restrict the returned properties.
    /// </summary>
    Task<IReadOnlyList<EnvironmentManagementSetting>> ListAsync(
        string? profileName,
        string? selectFilter,
        CancellationToken ct);

    /// <summary>
    /// Updates a single environment management setting via PATCH.
    /// The <paramref name="value"/> string is auto-coerced to the
    /// appropriate JSON type (bool / int / string).
    /// </summary>
    Task UpdateAsync(
        string? profileName,
        string settingName,
        string value,
        CancellationToken ct);
}
