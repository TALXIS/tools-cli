using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reads solution component summaries and counts from the
/// <c>msdyn_solutioncomponentsummaries</c> and <c>msdyn_solutioncomponentcountsummaries</c>
/// virtual entities via Web API.
/// </summary>
internal static class SolutionComponentQueryReader
{
    /// <summary>
    /// Lists components in a solution, optionally filtered by type and/or parent entity.
    /// </summary>
    public static async Task<IReadOnlyList<ComponentSummaryRow>> ListAsync(
        IOrganizationServiceAsync2 service,
        Guid solutionId,
        int? componentTypeFilter,
        string? entityFilter,
        int? top,
        CancellationToken ct)
    {
        if (service is not ServiceClient client)
            throw new InvalidOperationException("Component summary queries require a ServiceClient instance.");

        var filter = $"({DataverseSchema.MsdynSolutionComponentSummary.SolutionId} eq {solutionId})";
        if (componentTypeFilter.HasValue)
            filter += $" and (({DataverseSchema.MsdynSolutionComponentSummary.ComponentType} eq {componentTypeFilter.Value}))";
        if (!string.IsNullOrWhiteSpace(entityFilter))
            filter += $" and {DataverseSchema.MsdynSolutionComponentSummary.PrimaryEntityName} eq '{entityFilter.Replace("'", "''")}'";


        var selectColumns = string.Join(",",
            DataverseSchema.MsdynSolutionComponentSummary.ComponentType,
            DataverseSchema.MsdynSolutionComponentSummary.ComponentTypeName,
            DataverseSchema.MsdynSolutionComponentSummary.DisplayName,
            DataverseSchema.MsdynSolutionComponentSummary.Name,
            DataverseSchema.MsdynSolutionComponentSummary.ObjectId,
            DataverseSchema.MsdynSolutionComponentSummary.IsManaged,
            DataverseSchema.MsdynSolutionComponentSummary.IsCustomizable);

        var path = $"{DataverseSchema.MsdynSolutionComponentSummary.EntitySetName}" +
                   $"?$filter={Uri.EscapeDataString(filter)}" +
                   $"&$select={Uri.EscapeDataString(selectColumns)}" +
                   "&api-version=9.1" +
                   (top.HasValue ? $"&$top={top.Value}" : "");

        var headers = new Dictionary<string, List<string>>
        {
            ["Prefer"] = new() { "odata.maxpagesize=5000" },
        };

        var response = client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty, headers);
        response.EnsureSuccessStatusCode();

        var rows = new List<ComponentSummaryRow>();
        await ParsePageAsync(response, rows, ct).ConfigureAwait(false);

        // Follow @odata.nextLink for paging (server may return less than maxpagesize)
        while (!top.HasValue || rows.Count < top.Value)
        {
            var pageJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var pageDoc = JsonDocument.Parse(pageJson);
            if (!pageDoc.RootElement.TryGetProperty("@odata.nextLink", out var nextLink))
                break;

            var nextUrl = nextLink.GetString();
            if (string.IsNullOrWhiteSpace(nextUrl))
                break;

            // nextLink is absolute — extract the relative path for ExecuteWebRequest
            var uri = new Uri(nextUrl);
            var relativePath = uri.PathAndQuery.TrimStart('/');
            if (relativePath.StartsWith("api/data/", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath[(relativePath.IndexOf("/v", StringComparison.Ordinal) + 1)..];
            if (relativePath.StartsWith("v9", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath[(relativePath.IndexOf('/') + 1)..];

            response.Dispose();
            response = client.ExecuteWebRequest(HttpMethod.Get, relativePath, string.Empty, headers);
            response.EnsureSuccessStatusCode();
            await ParsePageAsync(response, rows, ct).ConfigureAwait(false);
        }

        response.Dispose();

        if (top.HasValue && rows.Count > top.Value)
            rows.RemoveRange(top.Value, rows.Count - top.Value);

        return rows;
    }

    /// <summary>
    /// Returns per-type component counts for a solution.
    /// Delegates to <see cref="SolutionDetailReader.QueryComponentCountsAsync"/>.
    /// </summary>
    public static Task<IReadOnlyList<ComponentCountRow>> CountAsync(
        IOrganizationServiceAsync2 service,
        Guid solutionId,
        CancellationToken ct)
        => SolutionDetailReader.QueryComponentCountsAsync(service, solutionId, ct);

    private static async Task ParsePageAsync(HttpResponseMessage response, List<ComponentSummaryRow> rows, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
                rows.Add(ParseSummaryRow(item));
        }
    }

    private static ComponentSummaryRow ParseSummaryRow(JsonElement item)
    {
        var typeName = GetStringOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.ComponentTypeName);
        var typeCode = GetIntOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.ComponentType);
        var displayName = GetStringOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.DisplayName);
        var name = GetStringOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.Name);
        var objectId = GetStringOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.ObjectId) ?? "";

        // Boolean fields on virtual entities may come as bool or string — handle both
        var managed = GetBoolOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.IsManaged);
        var customizable = GetBoolOrDefault(item, DataverseSchema.MsdynSolutionComponentSummary.IsCustomizable);

        return new ComponentSummaryRow(
            TypeName: typeName ?? typeCode.ToString(),
            TypeCode: typeCode,
            DisplayName: displayName,
            Name: name,
            ObjectId: objectId,
            Managed: managed,
            Customizable: customizable);
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

    private static string? GetStringOrDefault(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static bool GetBoolOrDefault(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(prop.GetString(), out var b) && b,
            _ => false,
        };
    }
}
