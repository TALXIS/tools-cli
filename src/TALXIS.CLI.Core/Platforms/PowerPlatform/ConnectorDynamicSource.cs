namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// The connector operation that resolves a dynamic parameter at design time, with its
/// arguments. An argument that points at another parameter renders as <c>$ref:name</c>.
/// </summary>
public sealed record ConnectorDynamicSource(
    string Kind,
    string OperationId,
    IReadOnlyDictionary<string, string> Arguments);
