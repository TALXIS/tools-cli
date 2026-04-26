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


        var path = $"{DataverseSchema.MsdynSolutionComponentSummary.EntitySetName}" +
                   $"?$filter={filter}" +
                   $"&$select={DataverseSchema.MsdynSolutionComponentSummary.ComponentType}," +
                   $"{DataverseSchema.MsdynSolutionComponentSummary.ComponentTypeName}," +
                   $"{DataverseSchema.MsdynSolutionComponentSummary.DisplayName}," +
                   $"{DataverseSchema.MsdynSolutionComponentSummary.Name}," +
                   $"{DataverseSchema.MsdynSolutionComponentSummary.ObjectId}," +
                   $"{DataverseSchema.MsdynSolutionComponentSummary.IsManaged}," +
                   $"{DataverseSchema.MsdynSolutionComponentSummary.IsCustomizable}" +
                   "&api-version=9.1" +
                   (top.HasValue ? $"&$top={top.Value}" : "");

        var headers = new Dictionary<string, List<string>>
        {
            ["Prefer"] = new() { "odata.maxpagesize=5000" },
        };

        using var response = client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty, headers);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var rows = new List<ComponentSummaryRow>();
        if (doc.RootElement.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
            {
                rows.Add(ParseSummaryRow(item));
            }
        }

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
