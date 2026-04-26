using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reads detailed solution information via SDK query + component counts
/// via the <c>msdyn_solutioncomponentcountsummaries</c> Web API virtual entity.
/// </summary>
internal static class SolutionDetailReader
{
    private static readonly ColumnSet SolutionColumns = new(
        "solutionid", "uniquename", "friendlyname", "version",
        "ismanaged", "installedon", "description");

    /// <summary>
    /// Retrieves a single solution's details plus per-type component counts.
    /// </summary>
    public static async Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> ShowAsync(
        IOrganizationServiceAsync2 service,
        string solutionUniqueName,
        CancellationToken ct)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (string.IsNullOrWhiteSpace(solutionUniqueName))
            throw new ArgumentException("Solution unique name is required.", nameof(solutionUniqueName));

        // 1. Query solution entity with publisher expand
        var query = new QueryExpression(DataverseSchema.Solution.EntityName)
        {
            ColumnSet = SolutionColumns,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);
        query.AddLink("publisher", "publisherid", "publisherid", JoinOperator.LeftOuter)
            .Columns = new ColumnSet("friendlyname", "customizationprefix");

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result.Entities.Count == 0)
            throw new InvalidOperationException($"Solution '{solutionUniqueName}' not found.");

        var entity = result.Entities[0];
        var detail = ToSolutionDetail(entity);

        // 2. Query component counts via Web API
        var counts = await QueryComponentCountsAsync(service, detail.Id, ct).ConfigureAwait(false);

        return (detail, counts);
    }

    /// <summary>
    /// Resolves a solution unique name to its GUID. Lightweight — fetches only the ID.
    /// </summary>
    public static async Task<Guid> ResolveIdAsync(
        IOrganizationServiceAsync2 service,
        string solutionUniqueName,
        CancellationToken ct)
    {
        var query = new QueryExpression(DataverseSchema.Solution.EntityName)
        {
            ColumnSet = new ColumnSet("solutionid"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result.Entities.Count == 0)
            throw new InvalidOperationException($"Solution '{solutionUniqueName}' not found.");

        return result.Entities[0].Id;
    }

    internal static async Task<IReadOnlyList<ComponentCountRow>> QueryComponentCountsAsync(
        IOrganizationServiceAsync2 service,
        Guid solutionId,
        CancellationToken ct)
    {
        if (service is not ServiceClient client)
            throw new InvalidOperationException("Component count queries require a ServiceClient instance.");

        var filter = $"{DataverseSchema.MsdynSolutionComponentCountSummary.SolutionId} eq {solutionId}";
        var path = $"{DataverseSchema.MsdynSolutionComponentCountSummary.EntitySetName}" +
                   $"?$filter={Uri.EscapeDataString(filter)}";

        var headers = new Dictionary<string, List<string>>
        {
            ["Prefer"] = new() { "odata.include-annotations=*" },
        };
        using var response = client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty, headers);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var rows = new List<ComponentCountRow>();
        if (doc.RootElement.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
            {
                var typeName = item.TryGetProperty(DataverseSchema.MsdynSolutionComponentCountSummary.ComponentLogicalName, out var tn)
                    ? tn.GetString() : null;
                var typeCode = GetIntOrDefault(item, DataverseSchema.MsdynSolutionComponentCountSummary.ComponentType);
                var total = GetIntOrDefault(item, DataverseSchema.MsdynSolutionComponentCountSummary.Total);

                if (total > 0)
                    rows.Add(new ComponentCountRow(typeName ?? typeCode.ToString(), typeCode, typeName, total));
            }
        }

        return rows.OrderByDescending(r => r.Count).ToList();
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

    private static SolutionDetail ToSolutionDetail(Entity entity)
    {
        // Publisher info comes from the linked entity (aliased columns)
        string? publisherName = null;
        string? publisherPrefix = null;
        foreach (var attr in entity.Attributes)
        {
            if (attr.Value is AliasedValue aliased)
            {
                if (attr.Key.EndsWith(".friendlyname"))
                    publisherName = aliased.Value as string;
                else if (attr.Key.EndsWith(".customizationprefix"))
                    publisherPrefix = aliased.Value as string;
            }
        }

        return new SolutionDetail(
            Id: entity.Id,
            UniqueName: entity.GetAttributeValue<string>("uniquename") ?? "(unknown)",
            FriendlyName: entity.GetAttributeValue<string>("friendlyname"),
            Version: entity.GetAttributeValue<string>("version"),
            Managed: entity.GetAttributeValue<bool>("ismanaged"),
            InstalledOn: entity.GetAttributeValue<DateTime?>("installedon"),
            Description: entity.GetAttributeValue<string>("description"),
            PublisherName: publisherName,
            PublisherPrefix: publisherPrefix);
    }
}
