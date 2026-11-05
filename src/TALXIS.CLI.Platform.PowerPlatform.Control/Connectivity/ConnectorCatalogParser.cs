using System.Text.Json;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>A connector as the connectivity catalog lists it.</summary>
/// <param name="ApiId">Short connector id the connectivity routes take.</param>
/// <param name="XrmConnectorId">Dataverse <c>connector</c> row id of a custom connector.</param>
internal sealed record CatalogConnector(
    string ApiId,
    string? DisplayName,
    bool IsCustom,
    Guid? XrmConnectorId,
    string? Tier,
    string? Description);

/// <summary>Reads the payload of <c>/connectivity/connectors</c>.</summary>
internal static class ConnectorCatalogParser
{
    public static IReadOnlyList<CatalogConnector> Parse(JsonElement root)
    {
        var connectors = new List<CatalogConnector>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return connectors;

        foreach (var item in value.EnumerateArray())
        {
            var name = JsonElementReader.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var props = item.TryGetProperty("properties", out var p) ? p : default;
            connectors.Add(new CatalogConnector(
                name,
                JsonElementReader.TryGetString(props, "displayName"),
                props.ValueKind == JsonValueKind.Object
                    && props.TryGetProperty("isCustomApi", out var custom)
                    && custom.ValueKind == JsonValueKind.True,
                Guid.TryParse(JsonElementReader.TryGetString(props, "xrmConnectorId"), out var rowId) ? rowId : null,
                JsonElementReader.TryGetString(props, "tier"),
                JsonElementReader.TryGetString(props, "description")));
        }

        return connectors;
    }
}
