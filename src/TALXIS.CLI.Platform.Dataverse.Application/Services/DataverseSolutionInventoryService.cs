using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Dataverse.Application;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Dataverse implementation of <see cref="ISolutionInventoryService"/>.
/// Combines profile/connection resolution with the raw <c>solution</c> table
/// query so feature commands only see the abstraction.
/// </summary>
internal sealed class DataverseSolutionInventoryService : ISolutionInventoryService
{
    public async Task<IReadOnlyList<InstalledSolutionRecord>> ListAsync(
        string? profileName,
        bool? managedFilter,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await SolutionInventoryReader.ListAsync(conn.Client, managedFilter, ct: ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Reads the currently installed solution inventory from the Dataverse
/// <c>solution</c> table. Kept as a standalone helper so existing unit tests
/// (which mock <see cref="IOrganizationServiceAsync2"/> directly) can keep
/// exercising the query without going through the service-locator path.
/// </summary>
internal static class SolutionInventoryReader
{
    private const string EntityName = DataverseSchema.Solution.EntityName;
    private static readonly ColumnSet Columns = new(
        "solutionid",
        "uniquename",
        "friendlyname",
        "version",
        "ismanaged");

    public static async Task<IReadOnlyList<InstalledSolutionRecord>> ListAsync(
        IOrganizationServiceAsync2 service,
        bool? managedOnly = null,
        int maxRows = 5000,
        CancellationToken ct = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (maxRows <= 0) throw new ArgumentOutOfRangeException(nameof(maxRows), "maxRows must be > 0.");

        var query = new QueryExpression(EntityName)
        {
            ColumnSet = Columns,
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = maxRows,
        };
        query.AddOrder("friendlyname", OrderType.Ascending);
        query.AddOrder("uniquename", OrderType.Ascending);

        if (managedOnly is { } managed)
        {
            query.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, managed);
        }

        var response = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return response.Entities
            .Select(ToRecord)
            .Where(r => !string.IsNullOrWhiteSpace(r.UniqueName))
            .ToList();
    }

    private static InstalledSolutionRecord ToRecord(Entity entity)
    {
        return new InstalledSolutionRecord(
            Id: entity.Id,
            UniqueName: entity.GetAttributeValue<string>("uniquename") ?? "(unknown)",
            FriendlyName: entity.GetAttributeValue<string>("friendlyname"),
            Version: entity.GetAttributeValue<string>("version"),
            Managed: entity.GetAttributeValue<bool>("ismanaged"));
    }
}
