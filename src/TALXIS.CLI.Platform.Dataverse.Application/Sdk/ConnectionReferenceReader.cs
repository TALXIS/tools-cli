using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

internal static class ConnectionReferenceReader
{
    private static readonly ColumnSet Columns = new(
        "connectionreferenceid", "connectionreferencelogicalname", "connectionreferencedisplayname",
        "connectorid", "connectionid", "statecode");

    public static async Task<IReadOnlyList<ConnectionReferenceRecord>> ListAsync(
        IOrganizationServiceAsync2 service, string? connector, CancellationToken ct)
    {
        var query = new QueryExpression("connectionreference")
        {
            ColumnSet = Columns,
            TopCount = 5000,
        };

        // connectorid holds the full api id a flow definition also uses.
        if (!string.IsNullOrWhiteSpace(connector))
            query.Criteria.AddCondition("connectorid", ConditionOperator.Equal, $"/providers/Microsoft.PowerApps/apis/{connector}");

        query.AddOrder("connectionreferencelogicalname", OrderType.Ascending);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("connectionreferencelogicalname")))
            .Select(ToRecord)
            .ToList();
    }

    private static ConnectionReferenceRecord ToRecord(Entity e) => new(
        Id: e.Id,
        LogicalName: e.GetAttributeValue<string>("connectionreferencelogicalname"),
        DisplayName: e.GetAttributeValue<string>("connectionreferencedisplayname"),
        ConnectorId: e.GetAttributeValue<string>("connectorid"),
        ConnectionId: e.GetAttributeValue<string>("connectionid"),
        IsActive: e.GetAttributeValue<OptionSetValue>("statecode")?.Value is null or 0);
}
