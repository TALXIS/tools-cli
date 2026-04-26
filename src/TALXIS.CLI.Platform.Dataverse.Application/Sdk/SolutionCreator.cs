using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Creates unmanaged solutions in Dataverse.
/// </summary>
internal static class SolutionCreator
{
    public static async Task<SolutionCreateOutcome> CreateAsync(
        IOrganizationServiceAsync2 service,
        SolutionCreateOptions options,
        CancellationToken ct)
    {
        // Resolve publisher unique name to ID
        var publisherId = await ResolvePublisherIdAsync(service, options.PublisherUniqueName, ct).ConfigureAwait(false);

        var solution = new Entity(DataverseSchema.Solution.EntityName)
        {
            ["uniquename"] = options.UniqueName,
            ["friendlyname"] = options.DisplayName,
            ["publisherid"] = new EntityReference("publisher", publisherId),
            ["version"] = options.Version,
        };

        if (!string.IsNullOrWhiteSpace(options.Description))
            solution["description"] = options.Description;

        var id = await service.CreateAsync(solution, ct).ConfigureAwait(false);

        return new SolutionCreateOutcome(id, options.UniqueName, options.Version);
    }

    private static async Task<Guid> ResolvePublisherIdAsync(
        IOrganizationServiceAsync2 service,
        string publisherUniqueName,
        CancellationToken ct)
    {
        var query = new QueryExpression("publisher")
        {
            ColumnSet = new ColumnSet("publisherid"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 2,
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, publisherUniqueName);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result.Entities.Count == 0)
            throw new InvalidOperationException($"Publisher '{publisherUniqueName}' not found.");

        return result.Entities[0].Id;
    }
}
