namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// One operation (action or trigger) exposed by a connector, distilled from
/// its OpenAPI definition.
/// </summary>
public sealed record ConnectorOperationSummary(
    string OperationId,
    string Kind,
    string? Summary,
    string? Visibility,
    string? Description);
