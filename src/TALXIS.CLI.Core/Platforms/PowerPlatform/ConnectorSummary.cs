namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A connector available in a Power Platform environment (e.g. shared_teams).
/// </summary>
public sealed record ConnectorSummary(
    string Name,
    string DisplayName,
    string? Tier,
    bool IsCustomApi,
    string? Description);
