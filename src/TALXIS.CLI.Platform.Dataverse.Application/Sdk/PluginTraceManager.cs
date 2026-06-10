using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Reads and writes the organization-wide plugin trace log level
/// (<c>organization.plugintracelogsetting</c>).
/// </summary>
internal static class PluginTraceManager
{
    public static async Task<PluginTraceSetting> GetSettingAsync(
        IOrganizationServiceAsync2 service, CancellationToken ct)
    {
        var org = await RetrieveOrganizationAsync(service, ct).ConfigureAwait(false);
        return Map(org);
    }

    public static async Task<PluginTraceSetting> SetSettingAsync(
        IOrganizationServiceAsync2 service, PluginTraceLevel level, CancellationToken ct)
    {
        var org = await RetrieveOrganizationAsync(service, ct).ConfigureAwait(false);
        var update = new Entity("organization", org.Id)
        {
            ["plugintracelogsetting"] = new OptionSetValue((int)level),
        };
        await service.UpdateAsync(update, ct).ConfigureAwait(false);
        return Map(org) with { Level = level };
    }

    private static async Task<Entity> RetrieveOrganizationAsync(
        IOrganizationServiceAsync2 service, CancellationToken ct)
    {
        var query = new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("organizationid", "name", "plugintracelogsetting"),
            TopCount = 1,
        };
        var result = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var org = result.Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("Could not read the organization record to determine the plugin trace setting.");
        return org;
    }

    private static PluginTraceSetting Map(Entity e) => new(
        OrganizationId: e.Id,
        OrganizationName: e.GetAttributeValue<string>("name"),
        Level: (PluginTraceLevel)(e.GetAttributeValue<OptionSetValue>("plugintracelogsetting")?.Value ?? 0));
}
