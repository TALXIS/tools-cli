namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// Reads connector metadata from a connector's own Swagger 2.0 definition,
/// such as a workspace custom connector's <c>apiDefinition.swagger.json</c>,
/// with no environment involved.
/// </summary>
/// <remarks>
/// The answers have the shape <see cref="IConnectorMetadataService"/> gives
/// for the same connector, so a caller can use either source without telling
/// them apart.
/// </remarks>
public interface IConnectorDefinitionReader
{
    /// <summary>Indexes the definition's operations, optionally filtered by keyword.</summary>
    /// <param name="swaggerJson">The Swagger 2.0 document.</param>
    /// <param name="connector">The connector's logical name, used for <see cref="ConnectorDetail.LogicalName"/> and <see cref="ConnectorDetail.ApiId"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="swaggerJson"/> is not a JSON object.</exception>
    ConnectorDetail ReadConnector(string swaggerJson, string connector, string? search);

    /// <summary>Reads one operation's parameters, outputs and action type.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="swaggerJson"/> is not a JSON object, or it has no operation <paramref name="operationId"/>.
    /// </exception>
    OperationDetail ReadOperation(string swaggerJson, string connector, string operationId);
}
