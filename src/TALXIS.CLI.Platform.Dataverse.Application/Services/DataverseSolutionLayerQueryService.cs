using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="ISolutionLayerQueryService"/>.
/// Queries the <c>msdyn_componentlayers</c> virtual entity via Web API.
/// </summary>
internal sealed class DataverseSolutionLayerQueryService : ISolutionLayerQueryService
{
    public async Task<IReadOnlyList<ComponentLayerRow>> ListLayersAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await QueryLayersAsync(conn.Client, componentId, componentTypeName, filterActive: null, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetActiveLayerJsonAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var layers = await QueryLayersAsync(conn.Client, componentId, componentTypeName, filterActive: "Active", ct).ConfigureAwait(false);
        return layers.Count > 0 ? layers[0].ComponentJson : null;
    }

    private static async Task<IReadOnlyList<ComponentLayerRow>> QueryLayersAsync(
        IOrganizationServiceAsync2 service,
        string componentId,
        string componentTypeName,
        string? filterActive,
        CancellationToken ct)
    {
        if (service is not ServiceClient client)
            throw new InvalidOperationException("Layer queries require a ServiceClient instance.");

        var filter = $"({DataverseSchema.MsdynComponentLayer.ComponentId} eq '{componentId.Replace("'", "''")}'" +
                     $" and {DataverseSchema.MsdynComponentLayer.SolutionComponentName} eq '{componentTypeName.Replace("'", "''")}')";

        if (!string.IsNullOrWhiteSpace(filterActive))
            filter += $" and {DataverseSchema.MsdynComponentLayer.SolutionName} eq '{filterActive}'";

        // Note: $select does NOT reduce payload — msdyn_componentjson and msdyn_changes are always returned.
        var path = $"{DataverseSchema.MsdynComponentLayer.EntitySetName}?$filter={Uri.EscapeDataString(filter)}";

        var headers = new Dictionary<string, List<string>>
        {
            ["Prefer"] = new() { "odata.include-annotations=*" },
        };

        using var response = client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty, headers);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var rows = new List<ComponentLayerRow>();
        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                rows.Add(new ComponentLayerRow(
                    Order: GetIntOrDefault(item, DataverseSchema.MsdynComponentLayer.Order),
                    SolutionName: GetStringOrDefault(item, DataverseSchema.MsdynComponentLayer.SolutionName) ?? "(unknown)",
                    PublisherName: GetStringOrDefault(item, DataverseSchema.MsdynComponentLayer.PublisherName),
                    Name: GetStringOrDefault(item, DataverseSchema.MsdynComponentLayer.Name),
                    OverwriteTime: item.TryGetProperty(DataverseSchema.MsdynComponentLayer.OverwriteTime, out var ot)
                        ? ot.GetDateTime() : DateTime.MinValue,
                    ComponentJson: GetStringOrDefault(item, DataverseSchema.MsdynComponentLayer.ComponentJson),
                    Changes: GetStringOrDefault(item, DataverseSchema.MsdynComponentLayer.Changes)));
            }
        }

        return rows.OrderBy(r => r.Order).ToList();
    }

    private static string? GetStringOrDefault(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int GetIntOrDefault(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return 0;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String => int.TryParse(prop.GetString(), out var i) ? i : 0,
            _ => 0,
        };
    }
}
