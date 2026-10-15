using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>Raw (status-less) connection reference row.</summary>
internal sealed record ConnectionReferenceRow(
    Guid Id,
    string LogicalName,
    string? DisplayName,
    string StoredConnectorId,
    Guid? CustomConnectorId,
    string? ConnectionId,
    bool IsManaged,
    string? Owner);

/// <summary>Dataverse access for the <c>connectionreference</c> table.</summary>
internal static class ConnectionReferenceManager
{
    private const string Table = "connectionreference";

    /// <summary>
    /// The solution component type of a connection reference. It is not in the
    /// component registry of the metadata package, so it is named here.
    /// </summary>
    public const int ComponentType = 10164;

    private static readonly ColumnSet Columns = new(
        "connectionreferenceid", "connectionreferencelogicalname", "connectionreferencedisplayname",
        "connectorid", "customconnectorid", "connectionid", "ismanaged", "ownerid");

    public static async Task<IReadOnlyList<ConnectionReferenceRow>> ListAsync(
        IOrganizationServiceAsync2 service,
        string? solutionUniqueName,
        string? storedConnectorId,
        Guid? customConnectorId,
        string? connectionId,
        string? nameFilter,
        CancellationToken ct)
    {
        var query = new QueryExpression(Table) { ColumnSet = Columns, TopCount = 5000 };
        query.AddOrder("connectionreferencelogicalname", OrderType.Ascending);

        if (!string.IsNullOrWhiteSpace(storedConnectorId) || customConnectorId.HasValue)
        {
            var connector = new FilterExpression(LogicalOperator.Or);
            if (!string.IsNullOrWhiteSpace(storedConnectorId))
                connector.AddCondition("connectorid", ConditionOperator.Equal, storedConnectorId);
            if (customConnectorId.HasValue)
                connector.AddCondition("customconnectorid", ConditionOperator.Equal, customConnectorId.Value);
            query.Criteria.AddFilter(connector);
        }

        if (!string.IsNullOrWhiteSpace(connectionId))
            query.Criteria.AddCondition("connectionid", ConditionOperator.Equal, connectionId);

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var either = new FilterExpression(LogicalOperator.Or);
            either.AddCondition("connectionreferencelogicalname", ConditionOperator.Like, $"%{nameFilter}%");
            either.AddCondition("connectionreferencedisplayname", ConditionOperator.Like, $"%{nameFilter}%");
            query.Criteria.AddFilter(either);
        }

        if (!string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            // connectionreference -> solutioncomponent (component type 10164) -> solution
            var component = query.AddLink("solutioncomponent", "connectionreferenceid", "objectid", JoinOperator.Inner);
            component.LinkCriteria.AddCondition("componenttype", ConditionOperator.Equal, ComponentType);
            var solution = component.AddLink("solution", "solutionid", "solutionid", JoinOperator.Inner);
            solution.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);
        }

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Select(ToRow).DistinctBy(r => r.Id).ToList();
    }

    public static async Task<ConnectionReferenceRow?> FindAsync(
        IOrganizationServiceAsync2 service, string logicalName, CancellationToken ct)
    {
        var query = new QueryExpression(Table) { ColumnSet = Columns, TopCount = 2 };
        query.Criteria.AddCondition("connectionreferencelogicalname", ConditionOperator.Equal, logicalName);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0 ? ToRow(result.Entities[0]) : null;
    }

    public static async Task<bool> IsInSolutionAsync(
        IOrganizationServiceAsync2 service, Guid id, string solutionUniqueName, CancellationToken ct)
    {
        var query = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet(false), TopCount = 1 };
        query.Criteria.AddCondition("objectid", ConditionOperator.Equal, id);
        query.Criteria.AddCondition("componenttype", ConditionOperator.Equal, ComponentType);
        var solution = query.AddLink("solution", "solutionid", "solutionid", JoinOperator.Inner);
        solution.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0;
    }

    public static Task AddToSolutionAsync(
        IOrganizationServiceAsync2 service, Guid id, string solutionUniqueName, CancellationToken ct)
        => SolutionComponentMutator.AddAsync(
            service,
            new ComponentAddOptions(
                solutionUniqueName, id, ComponentType, AddRequiredComponents: false, DoNotIncludeSubcomponents: false),
            ct);

    /// <summary>
    /// Creates the reference. The platform requires the path-form connector id
    /// even for custom connectors, whose row is additionally linked.
    /// </summary>
    public static async Task<Guid> CreateAsync(
        IOrganizationServiceAsync2 service,
        ConnectionReferenceCreateOptions options,
        string storedConnectorId,
        Guid? customConnectorId,
        CancellationToken ct)
    {
        var entity = new Entity(Table)
        {
            ["connectionreferencelogicalname"] = options.LogicalName,
            ["connectionreferencedisplayname"] = options.DisplayName,
            ["connectorid"] = storedConnectorId,
        };

        if (customConnectorId.HasValue)
            entity["customconnectorid"] = new EntityReference("connector", customConnectorId.Value);
        if (!string.IsNullOrWhiteSpace(options.ConnectionId))
            entity["connectionid"] = options.ConnectionId;

        var request = new CreateRequest { Target = entity };
        if (!string.IsNullOrWhiteSpace(options.SolutionUniqueName))
            request.Parameters["SolutionUniqueName"] = options.SolutionUniqueName;

        var response = (CreateResponse)await service.ExecuteAsync(request, ct).ConfigureAwait(false);
        return response.id;
    }

    public static async Task UpdateAsync(
        IOrganizationServiceAsync2 service, Guid id, string? connectionId, string? displayName, CancellationToken ct)
    {
        var entity = new Entity(Table, id);
        if (!string.IsNullOrWhiteSpace(connectionId))
            entity["connectionid"] = connectionId;
        if (!string.IsNullOrWhiteSpace(displayName))
            entity["connectionreferencedisplayname"] = displayName;

        await service.UpdateAsync(entity, ct).ConfigureAwait(false);
    }

    public static Task DeleteAsync(IOrganizationServiceAsync2 service, Guid id, CancellationToken ct)
        => service.DeleteAsync(Table, id, ct);

    private static ConnectionReferenceRow ToRow(Entity e) => new(
        e.Id,
        e.GetAttributeValue<string>("connectionreferencelogicalname") ?? string.Empty,
        e.GetAttributeValue<string>("connectionreferencedisplayname"),
        e.GetAttributeValue<string>("connectorid") ?? string.Empty,
        e.GetAttributeValue<EntityReference>("customconnectorid")?.Id,
        e.GetAttributeValue<string>("connectionid"),
        e.GetAttributeValue<bool>("ismanaged"),
        e.GetAttributeValue<EntityReference>("ownerid")?.Name);
}
