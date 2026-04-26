using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseMetadataIdResolver : IMetadataIdResolver
{
    public async Task<Guid> ResolveEntityIdAsync(string? profileName, string entityLogicalName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        if (conn.Client is not ServiceClient client)
            throw new InvalidOperationException("Metadata resolution requires a ServiceClient instance.");

        var path = $"EntityDefinitions?$filter={Uri.EscapeDataString($"LogicalName eq '{entityLogicalName}'")}&$select=MetadataId";
        var headers = new Dictionary<string, List<string>>();
        using var response = client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty, headers);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("MetadataId", out var mid))
                    return Guid.Parse(mid.GetString()!);
            }
        }

        throw new InvalidOperationException($"Entity '{entityLogicalName}' not found.");
    }

    public async Task<Guid> ResolveAttributeIdAsync(string? profileName, string entityLogicalName, string attributeLogicalName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        if (conn.Client is not ServiceClient client)
            throw new InvalidOperationException("Metadata resolution requires a ServiceClient instance.");

        var filter = Uri.EscapeDataString($"LogicalName eq '{attributeLogicalName}'");
        var path = $"EntityDefinitions(LogicalName='{entityLogicalName}')/Attributes?$filter={filter}&$select=MetadataId";
        var headers = new Dictionary<string, List<string>>();
        using var response = client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty, headers);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("MetadataId", out var mid))
                    return Guid.Parse(mid.GetString()!);
            }
        }

        throw new InvalidOperationException($"Attribute '{attributeLogicalName}' on entity '{entityLogicalName}' not found.");
    }
}
