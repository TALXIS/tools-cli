using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Shim that provides the legacy <c>CrmServiceClient</c> API surface
/// by inheriting from the modern <see cref="ServiceClient"/>.
/// CrmPackageCore and related assemblies reference
/// <c>Microsoft.Xrm.Tooling.Connector.CrmServiceClient</c> — this type
/// satisfies those references at runtime while using a fully modern
/// HTTP-based Dataverse transport (no WCF / legacy framework dependencies).
/// </summary>
public class CrmServiceClient : ServiceClient, IDisposable
{
    #region Nested types expected by CrmPackageCore

    public enum ImportStatus
    {
        NotImported,
        InProgress,
        Completed
    }

    public enum LogicalSearchOperator
    {
        None,
        And,
        Or
    }

    public enum LogicalSortOrder
    {
        Ascending,
        Descending
    }

    public sealed class CrmSearchFilter
    {
        public List<CrmFilterConditionItem> SearchConditions { get; set; } = new();
        public LogicalOperator FilterOperator { get; set; }
    }

    public sealed class CrmFilterConditionItem
    {
        public string FieldName { get; set; } = string.Empty;
        public object? FieldValue { get; set; }
        public ConditionOperator FieldOperator { get; set; }
    }

    public sealed class ImportRequest
    {
        public enum ImportMode
        {
            Create,
            Update
        }

        public string ImportName { get; set; } = string.Empty;
        public ImportMode Mode { get; set; }
        public Guid DataMapFileId { get; set; }
        public string DataMapFileName { get; set; } = string.Empty;
        public bool UseSystemMap { get; set; }
        public List<ImportFileItem> Files { get; set; } = new();
    }

    public class ImportFileItem
    {
        public enum DataDelimiterCode { DoubleQuotes = 1, None, SingleQuote }
        public enum FieldDelimiterCode { Colon = 1, Comma, SingleQuote }
        public enum FileTypeCode { CSV, XML }

        public string FileName { get; set; } = string.Empty;
        public FileTypeCode FileType { get; set; }
        public string FileContentToImport { get; set; } = string.Empty;
        public bool EnableDuplicateDetection { get; set; }
        public string SourceEntityName { get; set; } = string.Empty;
        public string TargetEntityName { get; set; } = string.Empty;
        public DataDelimiterCode DataDelimiter { get; set; }
        public FieldDelimiterCode FieldDelimiter { get; set; }
        public bool IsFirstRowHeader { get; set; }
        public bool IsRecordOwnerATeam { get; set; }
        public string RecordOwner { get; set; } = string.Empty;
    }

    #endregion

    #region Static properties matching legacy CrmServiceClient

    /// <summary>
    /// Auth hook used by PAC CLI and our interactive auth flow.
    /// CrmPackageCore sets this before constructing the client.
    /// </summary>
    public static IOverrideAuthHookWrapper? AuthOverrideHook { get; set; }

    public new static TimeSpan MaxConnectionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    #endregion

    #region Constructors

    /// <summary>
    /// Token-provider constructor — primary path for the CLI.
    /// </summary>
    public CrmServiceClient(Uri instanceUrl, Func<string, Task<string>> tokenProviderFunction, bool useUniqueInstance = true, Microsoft.Extensions.Logging.ILogger? logger = null)
        : base(instanceUrl, tokenProviderFunction, useUniqueInstance, logger)
    {
    }

    /// <summary>
    /// Connection string constructor — fallback for --connection-string.
    /// </summary>
    public CrmServiceClient(string dataverseConnectionString)
        : base(dataverseConnectionString)
    {
    }

    /// <summary>
    /// URI + useUniqueInstance constructor.
    /// Legacy code: <c>new CrmServiceClient(environmentUrl, useUniqueInstance: true)</c>.
    /// Requires <see cref="AuthOverrideHook"/> to be set for token acquisition.
    /// </summary>
    public CrmServiceClient(Uri instanceUrl, bool useUniqueInstance)
        : base(instanceUrl, CreateTokenProviderFromAuthHook(instanceUrl), useUniqueInstance)
    {
    }

    #endregion

    #region Property aliases

    public string LastCrmError => LastError;
    public Exception? LastCrmException => LastException;
    public Uri CrmConnectOrgUriActual => ConnectedOrgUriActual;

    #endregion

    #region Convenience methods used by CrmPackageCore

    public OrganizationResponse ExecuteCrmOrganizationRequest(OrganizationRequest req, string logMessageTag = "User Defined")
    {
        return ExecuteOrganizationRequest(req, logMessageTag);
    }

    public Guid GetMyCrmUserId()
    {
        var response = Execute(new OrganizationRequest("WhoAmI"));
        return (Guid)response.Results["UserId"];
    }

    public T GetDataByKeyFromResultsSet<T>(Dictionary<string, object> results, string key)
    {
        if (results != null && results.TryGetValue(key, out var value))
        {
            return (T)value;
        }
        return default!;
    }

    public EntityCollection GetEntityDataByFetchSearchEC(string fetchXml, Guid batchId = default)
    {
        return RetrieveMultiple(new FetchExpression(fetchXml));
    }

    public EntityCollection GetEntityDataByFetchSearchEC(string fetchXml, int pageCount, int pageNumber, string pageCookie, out string outPageCookie, out bool isMoreRecords, Guid batchId = default)
    {
        string pagedFetch = InjectPagingIntoFetchXml(fetchXml, pageCount, pageNumber, pageCookie);
        var result = RetrieveMultiple(new FetchExpression(pagedFetch));
        outPageCookie = result.PagingCookie ?? string.Empty;
        isMoreRecords = result.MoreRecords;
        return result;
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataByFetchSearch(string fetchXml, Guid batchId = default)
    {
        var ec = GetEntityDataByFetchSearchEC(fetchXml, batchId);
        return EntityCollectionToDictionary(ec);
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataByFetchSearch(string fetchXml, int pageCount, int pageNumber, string pageCookie, out string outPageCookie, out bool isMoreRecords, Guid batchId = default)
    {
        var ec = GetEntityDataByFetchSearchEC(fetchXml, pageCount, pageNumber, pageCookie, out outPageCookie, out isMoreRecords, batchId);
        return EntityCollectionToDictionary(ec);
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataBySearchParams(string entityName, Dictionary<string, string> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList, Guid batchId = default)
    {
        var query = BuildQueryExpression(entityName, searchParameters, searchOperator, fieldList);
        var result = RetrieveMultiple(query);
        return EntityCollectionToDictionary(result);
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataBySearchParams(string entityName, List<CrmSearchFilter> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList, Guid batchId = default)
    {
        var query = BuildQueryExpression(entityName, searchParameters, searchOperator, fieldList);
        var result = RetrieveMultiple(query);
        return EntityCollectionToDictionary(result);
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataBySearchParams(string entityName, List<CrmSearchFilter> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList, Dictionary<string, LogicalSortOrder> sortParameters, int pageCount, int pageNumber, string pageCookie, out string outPageCookie, out bool isMoreRecords, Guid batchId = default)
    {
        var query = BuildQueryExpression(entityName, searchParameters, searchOperator, fieldList);

        if (sortParameters != null)
        {
            foreach (var kvp in sortParameters)
            {
                query.AddOrder(kvp.Key, kvp.Value == LogicalSortOrder.Descending ? OrderType.Descending : OrderType.Ascending);
            }
        }

        query.PageInfo = new PagingInfo
        {
            Count = pageCount,
            PageNumber = pageNumber,
            PagingCookie = pageCookie
        };

        var result = RetrieveMultiple(query);
        outPageCookie = result.PagingCookie ?? string.Empty;
        isMoreRecords = result.MoreRecords;
        return EntityCollectionToDictionary(result);
    }

    public Dictionary<string, object> GetEntityDataById(string searchEntity, Guid entityId, List<string> fieldList, Guid batchId = default)
    {
        var columns = (fieldList == null || fieldList.Count == 0) ? new ColumnSet(true) : new ColumnSet(fieldList.ToArray());
        try
        {
            var entity = Retrieve(searchEntity, entityId, columns);
            return EntityToDictionary(entity);
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataByLinkedSearch(string returnEntityName, Dictionary<string, string> primarySearchParameters, string linkedEntityName, Dictionary<string, string> linkedSearchParameters, string linkedEntityLinkAttribName, string m2MEntityName, string returnEntityPrimaryId, LogicalSearchOperator searchOperator, List<string> fieldList, Guid batchId = default)
    {
        var query = BuildLinkedQuery(returnEntityName, primarySearchParameters, linkedEntityName, linkedSearchParameters, linkedEntityLinkAttribName, m2MEntityName, returnEntityPrimaryId, searchOperator, fieldList);
        var result = RetrieveMultiple(query);
        return EntityCollectionToDictionary(result);
    }

    public Dictionary<string, Dictionary<string, object>> GetEntityDataByLinkedSearch(string returnEntityName, List<CrmSearchFilter> primarySearchParameters, string linkedEntityName, List<CrmSearchFilter> linkedSearchParameters, string linkedEntityLinkAttribName, string m2MEntityName, string returnEntityPrimaryId, LogicalSearchOperator searchOperator, List<string> fieldList, Guid batchId = default, bool isReflexiveRelationship = false)
    {
        // Simplified — convert CrmSearchFilter to simple dictionary for the primary entity
        var query = new QueryExpression(returnEntityName);
        if (fieldList != null && fieldList.Count > 0)
            query.ColumnSet = new ColumnSet(fieldList.ToArray());
        else
            query.ColumnSet = new ColumnSet(true);

        ApplyFilters(query.Criteria, primarySearchParameters, searchOperator);

        if (!string.IsNullOrEmpty(linkedEntityName))
        {
            var link = query.AddLink(linkedEntityName, returnEntityPrimaryId, linkedEntityLinkAttribName);
            if (!string.IsNullOrEmpty(m2MEntityName))
            {
                link = query.AddLink(m2MEntityName, returnEntityPrimaryId, returnEntityPrimaryId);
                link.AddLink(linkedEntityName, linkedEntityLinkAttribName, linkedEntityLinkAttribName);
            }

            if (linkedSearchParameters != null)
            {
                ApplyFilters(link.LinkCriteria, linkedSearchParameters, searchOperator);
            }
        }

        var result = RetrieveMultiple(query);
        return EntityCollectionToDictionary(result);
    }

    public Guid CreateNewRecord(string entityName, Dictionary<string, CrmDataTypeWrapper> valueArray, string applyToSolution = "", bool enabledDuplicateDetection = false, Guid batchId = default)
    {
        var entity = new Entity(entityName);
        PopulateEntityFromDataTypeWrappers(entity, valueArray);

        return Create(entity);
    }

    public bool UpdateEntity(string entityName, string keyFieldName, Guid id, Dictionary<string, CrmDataTypeWrapper> fieldList, string applyToSolution = "", bool enabledDuplicateDetection = false, Guid batchId = default)
    {
        try
        {
            var entity = new Entity(entityName, id);
            PopulateEntityFromDataTypeWrappers(entity, fieldList);
            Update(entity);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteEntity(string entityType, Guid entityId, Guid batchId = default)
    {
        try
        {
            Delete(entityType, entityId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteEntityAssociation(string entityName1, Guid entity1Id, string entityName2, Guid entity2Id, string relationshipName, Guid batchId = default)
    {
        try
        {
            Disassociate(entityName1, entity1Id, new Relationship(relationshipName), new EntityReferenceCollection { new EntityReference(entityName2, entity2Id) });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Guid DeleteAndPromoteSolutionAsync(string uniqueName)
    {
        var request = new OrganizationRequest("DeleteAndPromote");
        request["UniqueName"] = uniqueName;
        var response = Execute(request);
        return response.Results.TryGetValue("AsyncOperationId", out var asyncId) ? (Guid)asyncId : Guid.Empty;
    }

    public Guid ImportDataMapToCrm(string dataMapXml, bool replaceIds = true, bool dataMapXmlIsFilePath = false)
    {
        string xmlContent = dataMapXmlIsFilePath ? File.ReadAllText(dataMapXml) : dataMapXml;
        var request = new OrganizationRequest("ImportMappingsImportMap");
        request["MappingsXml"] = xmlContent;
        request["ReplaceIds"] = replaceIds;
        var response = Execute(request);
        return response.Results.TryGetValue("ImportMapId", out var id) ? (Guid)id : Guid.Empty;
    }

    public Guid InstallSampleDataToCrm()
    {
        var response = Execute(new OrganizationRequest("InstallSampleData"));
        return Guid.Empty;
    }

    public ImportStatus IsSampleDataInstalled()
    {
        try
        {
            var result = RetrieveMultiple(new FetchExpression(
                "<fetch top='1'><entity name='account'><attribute name='accountid'/>" +
                "<filter><condition attribute='name' operator='eq' value='Adventure Works (sample)'/></filter></entity></fetch>"));
            return result.Entities.Count > 0 ? ImportStatus.Completed : ImportStatus.NotImported;
        }
        catch
        {
            return ImportStatus.NotImported;
        }
    }

    public Guid SubmitImportRequest(ImportRequest importRequest, DateTime delayUntil)
    {
        var import = new Entity("import");
        import["name"] = importRequest.ImportName;
        import["modecode"] = new OptionSetValue(importRequest.Mode == ImportRequest.ImportMode.Create ? 0 : 1);
        var importId = Create(import);

        foreach (var file in importRequest.Files)
        {
            var importFile = new Entity("importfile");
            importFile["importid"] = new EntityReference("import", importId);
            importFile["name"] = file.FileName;
            importFile["source"] = file.FileContentToImport;
            importFile["sourceentityname"] = file.SourceEntityName;
            importFile["targetentityname"] = file.TargetEntityName;
            importFile["datadelimitercode"] = new OptionSetValue((int)file.DataDelimiter);
            importFile["fielddelimitercode"] = new OptionSetValue((int)file.FieldDelimiter);
            importFile["isfirstrowheader"] = file.IsFirstRowHeader;
            importFile["enableduplicatedetection"] = file.EnableDuplicateDetection;

            if (importRequest.DataMapFileId != Guid.Empty)
                importFile["importmapid"] = new EntityReference("importmap", importRequest.DataMapFileId);
            else if (importRequest.UseSystemMap)
                importFile["usesystemmap"] = true;

            Create(importFile);
        }

        var parseRequest = new OrganizationRequest("ParseImport");
        parseRequest["ImportId"] = importId;
        Execute(parseRequest);

        var transformRequest = new OrganizationRequest("TransformImport");
        transformRequest["ImportId"] = importId;
        Execute(transformRequest);

        var importRecords = new OrganizationRequest("ImportRecordsImport");
        importRecords["ImportId"] = importId;
        var response = Execute(importRecords);
        return response.Results.TryGetValue("AsyncOperationId", out var asyncId) ? (Guid)asyncId : importId;
    }

    #endregion

    #region Private helpers

    private static Func<string, Task<string>> CreateTokenProviderFromAuthHook(Uri instanceUrl)
    {
        return async (string resourceUrl) =>
        {
            var hook = AuthOverrideHook ?? throw new InvalidOperationException(
                "CrmServiceClient.AuthOverrideHook must be set before using the URI constructor. " +
                "Use the token provider or connection string constructor instead.");
            return hook.GetAuthToken(new Uri(resourceUrl));
        };
    }

    private static string InjectPagingIntoFetchXml(string fetchXml, int pageCount, int pageNumber, string pageCookie)
    {
        // Simple injection — replace <fetch with paging attributes
        if (string.IsNullOrWhiteSpace(fetchXml))
            return fetchXml;

        string pagingAttrs = $" count='{pageCount}' page='{pageNumber}'";
        if (!string.IsNullOrWhiteSpace(pageCookie))
            pagingAttrs += $" paging-cookie='{System.Security.SecurityElement.Escape(pageCookie)}'";

        return fetchXml.Replace("<fetch", $"<fetch{pagingAttrs}", StringComparison.OrdinalIgnoreCase);
    }

    private static QueryExpression BuildQueryExpression(string entityName, Dictionary<string, string> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList)
    {
        var query = new QueryExpression(entityName);
        if (fieldList != null && fieldList.Count > 0)
            query.ColumnSet = new ColumnSet(fieldList.ToArray());
        else
            query.ColumnSet = new ColumnSet(true);

        if (searchParameters != null && searchParameters.Count > 0)
        {
            query.Criteria.FilterOperator = searchOperator == LogicalSearchOperator.Or ? LogicalOperator.Or : LogicalOperator.And;
            foreach (var kvp in searchParameters)
            {
                query.Criteria.AddCondition(kvp.Key, ConditionOperator.Equal, kvp.Value);
            }
        }

        return query;
    }

    private static QueryExpression BuildQueryExpression(string entityName, List<CrmSearchFilter> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList)
    {
        var query = new QueryExpression(entityName);
        if (fieldList != null && fieldList.Count > 0)
            query.ColumnSet = new ColumnSet(fieldList.ToArray());
        else
            query.ColumnSet = new ColumnSet(true);

        if (searchParameters != null)
        {
            query.Criteria.FilterOperator = searchOperator == LogicalSearchOperator.Or ? LogicalOperator.Or : LogicalOperator.And;
            ApplyFilters(query.Criteria, searchParameters, searchOperator);
        }

        return query;
    }

    private static void ApplyFilters(FilterExpression criteria, List<CrmSearchFilter> filters, LogicalSearchOperator searchOperator)
    {
        foreach (var filter in filters)
        {
            var filterExpr = new FilterExpression(filter.FilterOperator);
            foreach (var condition in filter.SearchConditions)
            {
                if (condition.FieldValue != null)
                    filterExpr.AddCondition(condition.FieldName, condition.FieldOperator, condition.FieldValue);
                else
                    filterExpr.AddCondition(condition.FieldName, condition.FieldOperator);
            }
            criteria.AddFilter(filterExpr);
        }
    }

    private static QueryExpression BuildLinkedQuery(string returnEntityName, Dictionary<string, string> primarySearchParameters, string linkedEntityName, Dictionary<string, string> linkedSearchParameters, string linkedEntityLinkAttribName, string m2MEntityName, string returnEntityPrimaryId, LogicalSearchOperator searchOperator, List<string> fieldList)
    {
        var query = new QueryExpression(returnEntityName);
        if (fieldList != null && fieldList.Count > 0)
            query.ColumnSet = new ColumnSet(fieldList.ToArray());
        else
            query.ColumnSet = new ColumnSet(true);

        if (primarySearchParameters != null)
        {
            query.Criteria.FilterOperator = searchOperator == LogicalSearchOperator.Or ? LogicalOperator.Or : LogicalOperator.And;
            foreach (var kvp in primarySearchParameters)
                query.Criteria.AddCondition(kvp.Key, ConditionOperator.Equal, kvp.Value);
        }

        if (!string.IsNullOrEmpty(linkedEntityName))
        {
            LinkEntity link;
            if (!string.IsNullOrEmpty(m2MEntityName))
            {
                link = query.AddLink(m2MEntityName, returnEntityPrimaryId, returnEntityPrimaryId);
                link.AddLink(linkedEntityName, linkedEntityLinkAttribName, linkedEntityLinkAttribName);
            }
            else
            {
                link = query.AddLink(linkedEntityName, returnEntityPrimaryId, linkedEntityLinkAttribName);
            }

            if (linkedSearchParameters != null)
            {
                link.LinkCriteria.FilterOperator = searchOperator == LogicalSearchOperator.Or ? LogicalOperator.Or : LogicalOperator.And;
                foreach (var kvp in linkedSearchParameters)
                    link.LinkCriteria.AddCondition(kvp.Key, ConditionOperator.Equal, kvp.Value);
            }
        }

        return query;
    }

    private static Dictionary<string, Dictionary<string, object>> EntityCollectionToDictionary(EntityCollection ec)
    {
        var result = new Dictionary<string, Dictionary<string, object>>();
        foreach (var entity in ec.Entities)
        {
            result[entity.Id.ToString()] = EntityToDictionary(entity);
        }
        return result;
    }

    private static Dictionary<string, object> EntityToDictionary(Entity entity)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in entity.Attributes)
        {
            dict[attr.Key] = attr.Value;
        }
        if (!dict.ContainsKey(entity.LogicalName + "id"))
        {
            dict[entity.LogicalName + "id"] = entity.Id;
        }
        return dict;
    }

    private static void PopulateEntityFromDataTypeWrappers(Entity entity, Dictionary<string, CrmDataTypeWrapper> valueArray)
    {
        if (valueArray == null) return;

        foreach (var kvp in valueArray)
        {
            entity[kvp.Key] = ConvertCrmDataTypeValue(kvp.Value);
        }
    }

    private static object? ConvertCrmDataTypeValue(CrmDataTypeWrapper wrapper)
    {
        if (wrapper.Value == null) return null;

        return wrapper.Type switch
        {
            CrmFieldType.CrmBoolean => Convert.ToBoolean(wrapper.Value),
            CrmFieldType.CrmDateTime => Convert.ToDateTime(wrapper.Value),
            CrmFieldType.CrmDecimal => Convert.ToDecimal(wrapper.Value),
            CrmFieldType.CrmFloat => Convert.ToDouble(wrapper.Value),
            CrmFieldType.CrmMoney => new Money(Convert.ToDecimal(wrapper.Value)),
            CrmFieldType.CrmNumber => Convert.ToInt32(wrapper.Value),
            CrmFieldType.Key or CrmFieldType.UniqueIdentifier => wrapper.Value is Guid g ? g : Guid.Parse(wrapper.Value.ToString()!),
            CrmFieldType.Picklist => new OptionSetValue(Convert.ToInt32(wrapper.Value)),
            CrmFieldType.String => wrapper.Value.ToString(),
            CrmFieldType.Customer or CrmFieldType.Lookup => wrapper.Value is EntityReference er ? er : wrapper.Value,
            _ => wrapper.Value
        };
    }

    #endregion
}
