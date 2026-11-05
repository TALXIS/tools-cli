using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Reads the stable logical name of custom connectors (Dataverse
/// <c>connector.name</c>) from the <c>connector</c> table. Only a query that
/// reached Dataverse and failed there (no such table, no read privilege) counts
/// as "lookup unavailable"; connection and authentication failures surface.
/// </summary>
internal sealed class DataverseCustomConnectorNameSource : ICustomConnectorNameSource
{
    private const string Table = "connectors";
    private const string Columns = "connectorid,name,connectorinternalid";

    private readonly IDataverseQueryService _query;
    private readonly ILogger _logger;

    public DataverseCustomConnectorNameSource(IDataverseQueryService query, ILogger<DataverseCustomConnectorNameSource>? logger = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _logger = logger ?? NullLogger<DataverseCustomConnectorNameSource>.Instance;
    }

    public async Task<string?> TryFindApiIdAsync(string? profileName, string logicalName, CancellationToken ct)
    {
        var escaped = logicalName.Replace("'", "''", StringComparison.Ordinal);
        try
        {
            var result = await _query.QueryODataAsync(
                profileName, Table, Columns, $"name eq '{escaped}'",
                orderBy: null, top: 2, includeAnnotations: false, ct).ConfigureAwait(false);

            return result.Records
                .Select(record => String(record, "connectorinternalid"))
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        }
        catch (DataverseQueryException ex)
        {
            _logger.LogDebug(ex, "Could not look up connector logical name '{LogicalName}' in Dataverse.", logicalName);
            return null;
        }
    }

    public async Task<IReadOnlyList<CustomConnectorName>> ReadAllAsync(string? profileName, CancellationToken ct)
    {
        try
        {
            var result = await _query.QueryODataAsync(
                profileName, Table, Columns, filter: null,
                orderBy: null, top: null, includeAnnotations: false, ct).ConfigureAwait(false);

            return result.Records
                .Select(record => (Name: String(record, "name"), Row: String(record, "connectorid"), ApiId: String(record, "connectorinternalid")))
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new CustomConnectorName(r.Name!, Guid.TryParse(r.Row, out var id) ? id : null, string.IsNullOrWhiteSpace(r.ApiId) ? null : r.ApiId))
                .ToList();
        }
        catch (DataverseQueryException ex)
        {
            _logger.LogWarning(ex, "Could not read connector logical names from Dataverse; custom connectors are named by apiId.");
            return [];
        }
    }

    private static string? String(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}
