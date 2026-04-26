using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

internal static class PublisherManager
{
    private static readonly ColumnSet Columns = new(
        "publisherid", "uniquename", "friendlyname",
        "customizationprefix", "customizationoptionvalueprefix");

    public static async Task<IReadOnlyList<PublisherRecord>> ListAsync(
        IOrganizationServiceAsync2 service, CancellationToken ct)
    {
        var query = new QueryExpression("publisher")
        {
            ColumnSet = Columns,
            TopCount = 500,
        };
        query.AddOrder("friendlyname", OrderType.Ascending);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Select(ToRecord).ToList();
    }

    public static async Task<PublisherRecord?> ShowAsync(
        IOrganizationServiceAsync2 service, string uniqueName, CancellationToken ct)
    {
        var query = new QueryExpression("publisher")
        {
            ColumnSet = Columns,
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);

        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0 ? ToRecord(result.Entities[0]) : null;
    }

    public static async Task<Guid> CreateAsync(
        IOrganizationServiceAsync2 service, PublisherCreateOptions options, CancellationToken ct)
    {
        var entity = new Entity("publisher")
        {
            ["uniquename"] = options.UniqueName,
            ["friendlyname"] = options.FriendlyName,
            ["customizationprefix"] = options.CustomizationPrefix,
            ["customizationoptionvalueprefix"] = options.OptionValuePrefix,
        };

        if (!string.IsNullOrWhiteSpace(options.Description))
            entity["description"] = options.Description;

        return await service.CreateAsync(entity, ct).ConfigureAwait(false);
    }

    public static async Task DeleteAsync(
        IOrganizationServiceAsync2 service, string uniqueName, CancellationToken ct)
    {
        var pub = await ShowAsync(service, uniqueName, ct).ConfigureAwait(false);
        if (pub is null)
            throw new InvalidOperationException($"Publisher '{uniqueName}' not found.");

        await service.DeleteAsync("publisher", pub.Id, ct).ConfigureAwait(false);
    }

    private static PublisherRecord ToRecord(Entity e) => new(
        Id: e.Id,
        UniqueName: e.GetAttributeValue<string>("uniquename") ?? "(unknown)",
        FriendlyName: e.GetAttributeValue<string>("friendlyname"),
        CustomizationPrefix: e.GetAttributeValue<string>("customizationprefix"),
        OptionValuePrefix: e.GetAttributeValue<int>("customizationoptionvalueprefix"));
}
