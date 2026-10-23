namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// Full parameter-level detail of one connector operation, ready to be copied
/// into a cloud flow action (<c>host.apiId</c>, <c>host.operationId</c>,
/// <c>inputs.parameters</c> keys).
/// </summary>
public sealed record ConnectorOperationDetail(
    string ConnectorName,
    string ApiId,
    string OperationId,
    string Kind,
    string? Summary,
    string? Description,
    string HttpMethod,
    string Path,
    IReadOnlyList<ConnectorOperationParameter> Parameters);
