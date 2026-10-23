namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A single operation parameter. Body schema leaves are flattened into
/// slash-joined names (e.g. <c>item/subject</c>), exactly the key format a
/// cloud flow definition uses in <c>inputs.parameters</c>.
/// </summary>
public sealed record ConnectorOperationParameter(
    string Name,
    string In,
    string? Type,
    bool Required,
    string? Summary,
    string? Description,
    IReadOnlyList<string>? EnumValues,
    bool IsDynamic,
    ConnectorDynamicSource? DynamicSource = null);
