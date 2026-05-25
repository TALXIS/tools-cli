using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

internal static class PluginInventoryManager
{
    public static async Task<IReadOnlyList<PluginAssemblyRecord>> ListAssembliesAsync(
        IOrganizationServiceAsync2 service,
        string? nameContains,
        CancellationToken ct)
    {
        var query = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet(
                "pluginassemblyid", "name", "version", "culture", "publickeytoken",
                "isolationmode", "sourcetype", "description", "modifiedon"),
        };
        if (!string.IsNullOrWhiteSpace(nameContains))
            query.Criteria.AddCondition("name", ConditionOperator.Like, $"%{nameContains}%");
        query.AddOrder("name", OrderType.Ascending);

        var rows = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return rows.Select(MapAssembly).ToList();
    }

    public static async Task<IReadOnlyList<PluginTypeRecord>> ListTypesAsync(
        IOrganizationServiceAsync2 service,
        string? assemblyContains,
        PluginKind? kind,
        CancellationToken ct)
    {
        var query = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet(
                "plugintypeid", "typename", "friendlyname", "isworkflowactivity",
                "workflowactivitygroupname", "description", "pluginassemblyid"),
        };
        query.Criteria.AddCondition("typename", ConditionOperator.NotLike, "Compiled.Workflow%");

        if (kind == PluginKind.Plugin)
            query.Criteria.AddCondition("isworkflowactivity", ConditionOperator.Equal, false);
        else if (kind == PluginKind.WorkflowActivity)
            query.Criteria.AddCondition("isworkflowactivity", ConditionOperator.Equal, true);

        var assemblyLink = query.AddLink("pluginassembly", "pluginassemblyid", "pluginassemblyid", JoinOperator.Inner);
        assemblyLink.EntityAlias = "a";
        assemblyLink.Columns = new ColumnSet("pluginassemblyid", "name", "version");
        if (!string.IsNullOrWhiteSpace(assemblyContains))
            assemblyLink.LinkCriteria.AddCondition("name", ConditionOperator.Like, $"%{assemblyContains}%");

        query.AddOrder("typename", OrderType.Ascending);

        var rows = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return rows.Select(MapType).ToList();
    }

    public static async Task<IReadOnlyList<PluginStepRecord>> ListStepsAsync(
        IOrganizationServiceAsync2 service,
        string? assemblyContains,
        CancellationToken ct)
    {
        var query = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet(
                "sdkmessageprocessingstepid", "name", "description", "mode", "stage", "rank",
                "statecode", "filteringattributes", "configuration",
                "plugintypeid", "sdkmessageid", "sdkmessagefilterid"),
        };
        query.Criteria.AddCondition("stage", ConditionOperator.In, 10, 20, 40, 50);

        var typeLink = query.AddLink("plugintype", "plugintypeid", "plugintypeid", JoinOperator.Inner);
        typeLink.EntityAlias = "pt";
        typeLink.Columns = new ColumnSet("plugintypeid", "typename", "pluginassemblyid");

        var assemblyLink = typeLink.AddLink("pluginassembly", "pluginassemblyid", "pluginassemblyid", JoinOperator.Inner);
        assemblyLink.EntityAlias = "a";
        assemblyLink.Columns = new ColumnSet("pluginassemblyid", "name", "version");
        if (!string.IsNullOrWhiteSpace(assemblyContains))
            assemblyLink.LinkCriteria.AddCondition("name", ConditionOperator.Like, $"%{assemblyContains}%");

        var msgLink = query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.Inner);
        msgLink.EntityAlias = "msg";
        msgLink.Columns = new ColumnSet("name");

        var filterLink = query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter);
        filterLink.EntityAlias = "filter";
        filterLink.Columns = new ColumnSet("primaryobjecttypecode");

        query.AddOrder("rank", OrderType.Ascending);

        var rows = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return rows.Select(MapStep).ToList();
    }

    private static async Task<List<Entity>> RetrieveAllAsync(
        IOrganizationServiceAsync2 service, QueryExpression query, CancellationToken ct)
    {
        query.PageInfo ??= new PagingInfo();
        query.PageInfo.PageNumber = 1;
        query.PageInfo.PagingCookie = null;
        query.PageInfo.Count = 5000;

        var all = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            all.AddRange(page.Entities);
            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        return all;
    }

    private static PluginAssemblyRecord MapAssembly(Entity e) => new(
        Id: e.Id,
        Name: e.GetAttributeValue<string>("name") ?? "(unknown)",
        Version: e.GetAttributeValue<string>("version"),
        Culture: e.GetAttributeValue<string>("culture"),
        PublicKeyToken: e.GetAttributeValue<string>("publickeytoken"),
        IsolationMode: (PluginIsolationMode)(e.GetAttributeValue<OptionSetValue>("isolationmode")?.Value ?? 1),
        SourceType: (PluginAssemblySourceType)(e.GetAttributeValue<OptionSetValue>("sourcetype")?.Value ?? 0),
        Description: e.GetAttributeValue<string>("description"),
        ModifiedOn: e.GetAttributeValue<DateTime?>("modifiedon"));

    private static PluginTypeRecord MapType(Entity e)
    {
        var assemblyRef = e.GetAttributeValue<EntityReference>("pluginassemblyid");
        var assemblyId = assemblyRef?.Id ?? GetAliasedGuid(e, "a.pluginassemblyid");
        var isWorkflow = e.GetAttributeValue<bool>("isworkflowactivity");

        return new PluginTypeRecord(
            Id: e.Id,
            TypeName: e.GetAttributeValue<string>("typename") ?? "(unknown)",
            FriendlyName: e.GetAttributeValue<string>("friendlyname"),
            Kind: isWorkflow ? PluginKind.WorkflowActivity : PluginKind.Plugin,
            WorkflowActivityGroupName: e.GetAttributeValue<string>("workflowactivitygroupname"),
            Description: e.GetAttributeValue<string>("description"),
            AssemblyId: assemblyId,
            AssemblyName: GetAliasedString(e, "a.name") ?? assemblyRef?.Name ?? "(unknown)",
            AssemblyVersion: GetAliasedString(e, "a.version"));
    }

    private static PluginStepRecord MapStep(Entity e)
    {
        var ptRef = e.GetAttributeValue<EntityReference>("plugintypeid");
        var ptId = ptRef?.Id ?? GetAliasedGuid(e, "pt.plugintypeid");
        var ptName = GetAliasedString(e, "pt.typename") ?? ptRef?.Name ?? "(unknown)";
        var assemblyId = GetAliasedGuid(e, "a.pluginassemblyid");
        var assemblyName = GetAliasedString(e, "a.name") ?? "(unknown)";
        var assemblyVersion = GetAliasedString(e, "a.version");
        var message = GetAliasedString(e, "msg.name") ?? "(unknown)";
        var primaryEntity = GetAliasedString(e, "filter.primaryobjecttypecode");
        var stage = e.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0;
        var mode = e.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0;
        var statecode = e.GetAttributeValue<OptionSetValue>("statecode")?.Value;

        return new PluginStepRecord(
            Id: e.Id,
            Name: e.GetAttributeValue<string>("name") ?? "(unknown)",
            Description: e.GetAttributeValue<string>("description"),
            Message: message,
            PrimaryEntity: string.IsNullOrEmpty(primaryEntity) ? null : primaryEntity,
            Stage: (PluginStage)stage,
            Mode: (PluginExecutionMode)mode,
            Rank: e.GetAttributeValue<int>("rank"),
            Enabled: statecode == 0,
            FilteringAttributes: e.GetAttributeValue<string>("filteringattributes"),
            Configuration: e.GetAttributeValue<string>("configuration"),
            PluginTypeId: ptId,
            PluginTypeName: ptName,
            AssemblyId: assemblyId,
            AssemblyName: assemblyName,
            AssemblyVersion: assemblyVersion);
    }

    private static string? GetAliasedString(Entity e, string alias)
        => e.GetAttributeValue<AliasedValue>(alias)?.Value as string;

    private static Guid GetAliasedGuid(Entity e, string alias)
    {
        var av = e.GetAttributeValue<AliasedValue>(alias);
        return av?.Value is Guid g ? g : Guid.Empty;
    }
}
