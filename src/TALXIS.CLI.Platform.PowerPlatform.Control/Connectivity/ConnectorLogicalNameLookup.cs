using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>The logical names of the environment's custom connectors, by Dataverse row id and by apiId.</summary>
internal sealed record ConnectorLogicalNames(
    IReadOnlyDictionary<Guid, string> ByRowId,
    IReadOnlyDictionary<string, string> ByApiId)
{
    public static ConnectorLogicalNames None { get; } = new(
        new Dictionary<Guid, string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The apiId for a built-in connector, and for a custom one whose logical name is not known.</summary>
    public string LogicalNameOf(CatalogConnector connector)
    {
        if (!connector.IsCustom)
            return connector.ApiId;
        if (connector.XrmConnectorId is { } rowId && ByRowId.TryGetValue(rowId, out var byRow))
            return byRow;
        return ByApiId.TryGetValue(connector.ApiId, out var byApi) ? byApi : connector.ApiId;
    }
}

/// <summary>
/// Reads the stable logical name of custom connectors (Dataverse
/// <c>connector.name</c>) from the <c>connector</c> table. Only a query that
/// reached Dataverse and failed there (no such table, no read privilege) counts
/// as "lookup unavailable"; connection and authentication failures surface.
/// </summary>
internal sealed class ConnectorLogicalNameLookup
{
    private const string Table = "connectors";
    private const string Columns = "connectorid,name,connectorinternalid";

    private readonly IDataverseQueryService _query;
    private readonly ILogger _logger;

    public ConnectorLogicalNameLookup(IDataverseQueryService query, ILogger<ConnectorLogicalNameLookup>? logger = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _logger = logger ?? NullLogger<ConnectorLogicalNameLookup>.Instance;
    }

    /// <summary>The apiId of the custom connector with this logical name, or null when there is none or Dataverse cannot say.</summary>
    public async Task<string?> TryFindApiIdAsync(string? profileName, string logicalName, CancellationToken ct)
    {
        var escaped = logicalName.Replace("'", "''", StringComparison.Ordinal);
        try
        {
            var result = await _query.QueryODataAsync(
                profileName, Table, Columns, $"name eq '{escaped}'",
                orderBy: null, top: 2, includeAnnotations: false, ct).ConfigureAwait(false);

            return result.Records
                .Select(record => JsonElementReader.TryGetString(record, "connectorinternalid"))
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        }
        catch (DataverseQueryException ex)
        {
            _logger.LogDebug(ex, "Could not look up connector logical name '{LogicalName}' in Dataverse.", logicalName);
            return null;
        }
    }

    public async Task<ConnectorLogicalNames> ReadAllAsync(string? profileName, CancellationToken ct)
    {
        try
        {
            var result = await _query.QueryODataAsync(
                profileName, Table, Columns, filter: null,
                orderBy: null, top: null, includeAnnotations: false, ct).ConfigureAwait(false);

            return ToLogicalNames(result.Records);
        }
        catch (DataverseQueryException ex)
        {
            _logger.LogWarning(ex, "Could not read connector logical names from Dataverse; custom connectors are named by apiId.");
            return ConnectorLogicalNames.None;
        }
    }

    private static ConnectorLogicalNames ToLogicalNames(IReadOnlyList<JsonElement> records)
    {
        var byRow = new Dictionary<Guid, string>();
        var byApi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var name = JsonElementReader.TryGetString(record, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (Guid.TryParse(JsonElementReader.TryGetString(record, "connectorid"), out var rowId))
                byRow[rowId] = name;
            if (JsonElementReader.TryGetString(record, "connectorinternalid") is { Length: > 0 } internalId)
                byApi[internalId] = name;
        }

        return new ConnectorLogicalNames(byRow, byApi);
    }
}
