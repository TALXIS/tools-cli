using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Environment.Platforms.Dataverse;

/// <summary>
/// Lightweight inventory row for an installed Dataverse solution.
/// </summary>
public sealed record InstalledSolutionRecord(
    Guid Id,
    string UniqueName,
    string? FriendlyName,
    string? Version,
    bool Managed);

/// <summary>
/// Reads the currently installed solution inventory from the Dataverse <c>solution</c> table.
/// </summary>
public sealed class SolutionInventoryReader
{
    private const string EntityName = DataverseSchema.Solution.EntityName;
    private static readonly ColumnSet Columns = new(
        "solutionid",
        "uniquename",
        "friendlyname",
        "version",
        "ismanaged");

    private readonly IOrganizationServiceAsync2 _service;

    public SolutionInventoryReader(IOrganizationServiceAsync2 service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>
    /// Lists installed solutions, optionally filtering by managed/unmanaged type.
    /// </summary>
    public async Task<IReadOnlyList<InstalledSolutionRecord>> ListAsync(
        bool? managedOnly = null,
        int maxRows = 5000,
        CancellationToken ct = default)
    {
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

        var response = await _service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
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
