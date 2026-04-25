using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
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
        Completed,
        Failed
    }

    public enum LogicalSearchOperator
    {
        None,
        Or,
        And
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
        public Guid RecordOwner { get; set; }
    }

    #endregion

    #region Static properties matching legacy CrmServiceClient

    /// <summary>
    /// Auth hook used by PAC CLI and our interactive auth flow.
    /// CrmPackageCore sets this before constructing the client.
    /// </summary>
    public static IOverrideAuthHookWrapper? AuthOverrideHook { get; set; }

    public new static TimeSpan MaxConnectionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Legacy property. When set to <c>true</c>, requests should bypass
    /// custom plugin execution. The modern <see cref="ServiceClient"/> applies
    /// this per-request; we store the flag so CMT code that sets it does not
    /// throw <see cref="MissingMethodException"/>.
    /// </summary>
    public bool BypassPluginExecution { get; set; }

    /// <summary>
    /// Legacy property indicating whether the client supports forced metadata
    /// cache consistency. Stored but not enforced on the modern transport.
    /// </summary>
    public bool ForceServerMetadataCacheConsistency { get; set; }

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

    private Version? _cachedOrgVersion;

    /// <summary>
    /// Hides the base <see cref="ServiceClient.ConnectedOrgVersion"/> which
    /// returns the hardcoded default <c>9.0.0.0</c> when the token-provider
    /// constructor path is used (the SDK comments out
    /// <c>GetServerVersion</c>/<c>RefreshInstanceDetails</c> for
    /// <c>ExternalTokenManagement</c> auth).
    /// <para>
    /// This hidden property issues a <c>RetrieveVersion</c> request to obtain
    /// the real version from the response body. If that fails, it falls back
    /// to accessing <see cref="ServiceClient.OrganizationDetail"/> which
    /// triggers the SDK's lazy <c>RefreshInstanceDetails</c> call — that
    /// updates the internal <c>OrganizationVersion</c> as a side-effect.
    /// </para>
    /// </summary>
    public new Version ConnectedOrgVersion
    {
        get
        {
            if (_cachedOrgVersion != null)
                return _cachedOrgVersion;

            var baseVersion = base.ConnectedOrgVersion;
            if (baseVersion > new Version(9, 0, 0, 0))
            {
                _cachedOrgVersion = baseVersion;
                return baseVersion;
            }

            // Base returned 9.0.0.0 or lower — the SDK hardcodes this
            // default in the ExternalTokenManagement path and never queries
            // the server. Issue an explicit RetrieveVersion request.
            try
            {
                var response = Execute(new OrganizationRequest("RetrieveVersion"));
                if (response.Results.TryGetValue("Version", out var versionObj) &&
                    versionObj is string versionStr &&
                    Version.TryParse(versionStr, out var realVersion))
                {
                    _cachedOrgVersion = realVersion;
                    return realVersion;
                }
            }
            catch
            {
                // RetrieveVersion failed — fall through to the lazy-loading
                // fallback below.
            }

            // RetrieveVersion did not yield a usable version. Trigger the
            // SDK's own lazy-loading mechanism: accessing OrganizationDetail
            // causes ConnectionService.RefreshInstanceDetails to run, which
            // calls RetrieveCurrentOrganization and updates the internal
            // OrganizationVersion field.
            try
            {
                _ = OrganizationDetail;
                var refreshedVersion = base.ConnectedOrgVersion;
                if (refreshedVersion > new Version(9, 0, 0, 0))
                {
                    _cachedOrgVersion = refreshedVersion;
                    return refreshedVersion;
                }
            }
            catch
            {
                // OrganizationDetail may throw if the service is unreachable.
            }

            _cachedOrgVersion = baseVersion;
            return baseVersion;
        }
    }

    #endregion

    #region Execute override — BypassPluginExecution support

    /// <summary>
    /// Overrides <see cref="ServiceClient.Execute"/> to inject the
    /// <c>BypassCustomPluginExecution</c> request parameter when
    /// <see cref="BypassPluginExecution"/> is <c>true</c>. The legacy
    /// CrmServiceClient applied this globally; the modern SDK requires
    /// it per-request.
    /// </summary>
    public new OrganizationResponse Execute(OrganizationRequest request)
    {
        if (BypassPluginExecution &&
            !request.Parameters.ContainsKey("BypassCustomPluginExecution"))
        {
            request.Parameters["BypassCustomPluginExecution"] = true;
        }

        return base.Execute(request);
    }

    #endregion

    #region Convenience methods used by CrmPackageCore

    public OrganizationResponse ExecuteCrmOrganizationRequest(OrganizationRequest req, string logMessageTag = "User Defined")
    {
        return ExecuteOrganizationRequest(req, logMessageTag);
    }

    private Guid? _cachedUserId;

    public Guid GetMyCrmUserId()
    {
        if (_cachedUserId.HasValue)
            return _cachedUserId.Value;

        var response = Execute(new OrganizationRequest("WhoAmI"));
        _cachedUserId = (Guid)response.Results["UserId"];
        return _cachedUserId.Value;
    }

    /// <summary>
    /// Matches the original <c>CrmServiceClient.GetDataByKeyFromResultsSet&lt;T&gt;</c>
    /// behavior: attempts a direct type check first, falls back to the
    /// <c>_Property</c> entry (which holds the raw SDK typed value), and
    /// returns <c>default(T)</c> when the value type doesn't match.
    /// Never throws — CMT relies on this returning default for type mismatches
    /// (e.g. calling <c>GetDataByKeyFromResultsSet&lt;Guid&gt;</c> on a string field
    /// returns <c>Guid.Empty</c>).
    /// </summary>
    public T GetDataByKeyFromResultsSet<T>(Dictionary<string, object> results, string key)
    {
        try
        {
            if (results == null || !results.ContainsKey(key))
            {
                return default!;
            }

            // PICKLIST handling for int/string (matches original behavior)
            if (typeof(T) == typeof(int) || typeof(T) == typeof(string))
            {
                try
                {
                    string text = (string)results[key];
                    if (text.Contains("PICKLIST:"))
                    {
                        try
                        {
                            var parts = text.Split(':');
                            if (typeof(T) == typeof(int))
                            {
                                return (T)(object)Convert.ToInt32(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                            }
                            return (T)(object)parts[3];
                        }
                        catch
                        {
                            return (T)results[key];
                        }
                    }
                }
                catch
                {
                    if (results[key] is T val)
                    {
                        return val;
                    }
                }
            }

            // Direct type check — the primary happy path.
            if (results[key] is T typedValue)
            {
                return typedValue;
            }

            // Fallback: check _Property entry which holds the raw SDK typed value.
            // The original CrmServiceClient stores each attribute twice:
            // "key" → formatted string or raw value
            // "key_Property" → KeyValuePair<string, object> with the raw SDK value
            if (results.ContainsKey(key + "_Property"))
            {
                var kvp = (KeyValuePair<string, object>)results[key + "_Property"];
                if (kvp.Value is T propertyValue)
                {
                    return propertyValue;
                }
            }
        }
        catch
        {
            // Original logs "Error In GetDataByKeyFromResultsSet (Non-Fatal)"
            // and returns default — CMT depends on this never throwing.
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
            return null!;
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
        if (primarySearchParameters == null && linkedSearchParameters == null)
            return null!;

        primarySearchParameters ??= new List<CrmSearchFilter>();
        linkedSearchParameters ??= new List<CrmSearchFilter>();

        // Build primary entity filter
        var primaryFilter = new FilterExpression();
        ApplyFilters(primaryFilter, primarySearchParameters, searchOperator);

        // Build linked entity filter
        var linkedFilter = new FilterExpression();
        ApplyFilters(linkedFilter, linkedSearchParameters, searchOperator);

        // Build the link chain matching the original:
        // innermost link → linkedEntity, outermost link → m2MEntity (intersect)
        var innerLink = new LinkEntity();
        innerLink.LinkToEntityName = linkedEntityName;
        innerLink.LinkToAttributeName = linkedEntityLinkAttribName;
        innerLink.LinkFromAttributeName = isReflexiveRelationship
            ? $"{linkedEntityLinkAttribName}two"
            : linkedEntityLinkAttribName;
        innerLink.LinkCriteria = linkedFilter;

        var outerLink = new LinkEntity();
        outerLink.LinkToEntityName = m2MEntityName;
        outerLink.LinkToAttributeName = isReflexiveRelationship
            ? $"{returnEntityPrimaryId}one"
            : returnEntityPrimaryId;
        outerLink.LinkFromAttributeName = returnEntityPrimaryId;
        outerLink.LinkEntities.Add(innerLink);

        var query = new QueryExpression();
        query.EntityName = returnEntityName;
        if (fieldList != null && fieldList.Count > 0)
            query.ColumnSet = new ColumnSet(fieldList.ToArray());
        else
            query.ColumnSet = new ColumnSet(true);
        query.Criteria = primaryFilter;
        query.LinkEntities.Add(outerLink);

        var result = RetrieveMultiple(query);
        return EntityCollectionToDictionary(result);
    }

    public Guid CreateNewRecord(string entityName, Dictionary<string, CrmDataTypeWrapper> valueArray, string applyToSolution = "", bool enabledDuplicateDetection = false, Guid batchId = default)
    {
        var entity = new Entity(entityName);
        PopulateEntityFromDataTypeWrappers(entity, valueArray);

        var request = new CreateRequest { Target = entity };
        request.Parameters["SuppressDuplicateDetection"] = !enabledDuplicateDetection;
        if (!string.IsNullOrWhiteSpace(applyToSolution))
            request.Parameters["SolutionUniqueName"] = applyToSolution;

        var response = (CreateResponse)Execute(request);
        return response?.id ?? Guid.Empty;
    }

    public List<EntityMetadata> GetAllEntityMetadata(bool onlyPublished = true, EntityFilters filter = EntityFilters.Entity)
    {
        try
        {
            var response = (RetrieveAllEntitiesResponse)Execute(new RetrieveAllEntitiesRequest
            {
                EntityFilters = filter,
                RetrieveAsIfPublished = !onlyPublished
            });

            return response.EntityMetadata?.ToList() ?? new List<EntityMetadata>();
        }
        catch
        {
            return null!;
        }
    }

    public EntityMetadata GetEntityMetadata(string entityLogicalname, EntityFilters queryFilter = EntityFilters.Entity)
    {
        try
        {
            var response = (RetrieveEntityResponse)Execute(new RetrieveEntityRequest
            {
                LogicalName = entityLogicalname,
                EntityFilters = queryFilter,
                RetrieveAsIfPublished = false
            });

            return response.EntityMetadata;
        }
        catch
        {
            return null!;
        }
    }

    public List<AttributeMetadata> GetAllAttributesForEntity(string entityLogicalname)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalname))
        {
            return null!;
        }

        try
        {
            var response = (RetrieveEntityResponse)Execute(new RetrieveEntityRequest
            {
                LogicalName = entityLogicalname,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = false
            });

            return response.EntityMetadata?.Attributes?.ToList() ?? new List<AttributeMetadata>();
        }
        catch
        {
            return null!;
        }
    }

    public bool UpdateEntity(string entityName, string keyFieldName, Guid id, Dictionary<string, CrmDataTypeWrapper> fieldList, string applyToSolution = "", bool enabledDuplicateDetection = false, Guid batchId = default)
    {
        try
        {
            var entity = new Entity(entityName, id);
            PopulateEntityFromDataTypeWrappers(entity, fieldList);

            var request = new UpdateRequest { Target = entity };
            request.Parameters["SuppressDuplicateDetection"] = !enabledDuplicateDetection;
            if (!string.IsNullOrWhiteSpace(applyToSolution))
                request.Parameters["SolutionUniqueName"] = applyToSolution;

            Execute(request);
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

    public bool UpdateStateAndStatusForEntity(string entName, Guid id, string stateCode, string statusCode, Guid batchId = default)
    {
        try
        {
            return Microsoft.PowerPlatform.Dataverse.Client.Extensions.CRUDExtentions
                .UpdateStateAndStatusForEntity(this, entName, id, stateCode, statusCode, batchId);
        }
        catch
        {
            return false;
        }
    }

    public bool UpdateStateAndStatusForEntity(string entName, Guid id, int stateCode, int statusCode, Guid batchId = default)
    {
        try
        {
            return Microsoft.PowerPlatform.Dataverse.Client.Extensions.CRUDExtentions
                .UpdateStateAndStatusForEntity(this, entName, id, stateCode, statusCode, batchId);
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

    /// <summary>
    /// Assigns an entity record to a specific user.
    /// </summary>
    public bool AssignEntityToUser(Guid userId, string entityName, Guid entityId, Guid batchId = default)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.GeneralExtensions
            .AssignEntityToUser(this, userId, entityName, entityId, batchId);
    }

    /// <summary>
    /// Associates multiple entities of the same type to a single entity.
    /// </summary>
    public bool CreateMultiEntityAssociation(string targetEntity, Guid targetEntityId, string sourceEntityName, List<Guid> sourceEntitieIds, string relationshipName, Guid batchId = default, bool isReflexiveRelationship = false)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.CRUDExtentions
            .CreateMultiEntityAssociation(this, targetEntity, targetEntityId, sourceEntityName, sourceEntitieIds, relationshipName, batchId, isReflexiveRelationship);
    }

    /// <summary>
    /// Closes an opportunity as either Won or Lost.
    /// Status code 1 = Won (WinOpportunity), 2 = Lost (LoseOpportunity).
    /// Defaults to Won when an unrecognised status is provided.
    /// </summary>
    public Guid CloseOpportunity(Guid opportunityId, Dictionary<string, CrmDataTypeWrapper> closeData, int status, Guid batchId = default)
    {
        string requestName = status == 2 ? "LoseOpportunity" : "WinOpportunity";
        return ExecuteCloseRequest("opportunityclose", "opportunityid", "opportunity", opportunityId, closeData, status, requestName);
    }

    /// <summary>
    /// Closes an incident (case). Legacy convenience method implemented via Execute.
    /// </summary>
    public Guid CloseIncident(Guid incidentId, Dictionary<string, CrmDataTypeWrapper> closeData, int status, Guid batchId = default)
    {
        return ExecuteCloseRequest("incidentresolution", "incidentid", "incident", incidentId, closeData, status, "CloseIncident");
    }

    /// <summary>
    /// Closes a quote. Legacy convenience method implemented via Execute.
    /// </summary>
    public Guid CloseQuote(Guid quoteId, Dictionary<string, CrmDataTypeWrapper> closeData, int status, Guid batchId = default)
    {
        return ExecuteCloseRequest("quoteclose", "quoteid", "quote", quoteId, closeData, status, "CloseQuote");
    }

    /// <summary>
    /// Cancels a sales order. Legacy convenience method implemented via Execute.
    /// </summary>
    public Guid CancelSalesOrder(Guid orderId, Dictionary<string, CrmDataTypeWrapper> closeData, int status, Guid batchId = default)
    {
        return ExecuteCloseRequest("orderclose", "salesorderid", "salesorder", orderId, closeData, status, "CancelSalesOrder");
    }

    /// <summary>
    /// Shared helper for close/cancel operations. Creates an activity entity
    /// and executes the corresponding organization request.
    /// </summary>
    private Guid ExecuteCloseRequest(string closeEntityName, string regardingFieldName, string regardingEntityName, Guid regardingId, Dictionary<string, CrmDataTypeWrapper> closeData, int status, string requestName)
    {
        var closeEntity = new Entity(closeEntityName);
        closeEntity[regardingFieldName] = new EntityReference(regardingEntityName, regardingId);
        if (closeData != null)
        {
            foreach (var kvp in closeData)
            {
                closeEntity[kvp.Key] = kvp.Value?.Value;
            }
        }

        var request = new OrganizationRequest(requestName);
        request["Status"] = new OptionSetValue(status);

        // Each close request uses a different parameter name for the close entity
        string closeParamName = requestName switch
        {
            "WinOpportunity" or "LoseOpportunity" => "OpportunityClose",
            "CloseIncident" => "IncidentResolution",
            "CloseQuote" => "QuoteClose",
            "CancelSalesOrder" or "FulfillSalesOrder" => "OrderClose",
            _ => closeEntityName,
        };
        request[closeParamName] = closeEntity;

        var response = Execute(request);
        return response.Results.TryGetValue("id", out var id) ? (Guid)id : Guid.Empty;
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
        if (string.IsNullOrWhiteSpace(fetchXml))
            return fetchXml;

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(fetchXml);
        var root = doc.DocumentElement!;

        var pageAttr = doc.CreateAttribute("page");
        pageAttr.Value = pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var countAttr = doc.CreateAttribute("count");
        countAttr.Value = pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cookieAttr = doc.CreateAttribute("paging-cookie");
        cookieAttr.Value = pageCookie;

        root.Attributes.Append(pageAttr);
        root.Attributes.Append(countAttr);
        root.Attributes.Append(cookieAttr);

        return root.OuterXml;
    }

    private static QueryExpression BuildQueryExpression(string entityName, Dictionary<string, string> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList)
    {
        var query = new QueryExpression(entityName);
        query.NoLock = true;
        if (fieldList != null && fieldList.Count > 0)
            query.ColumnSet = new ColumnSet(fieldList.ToArray());
        else
            query.ColumnSet = new ColumnSet(true);

        if (searchParameters != null && searchParameters.Count > 0)
        {
            query.Criteria.FilterOperator = searchOperator == LogicalSearchOperator.Or ? LogicalOperator.Or : LogicalOperator.And;
            foreach (var kvp in searchParameters)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value))
                    query.Criteria.AddCondition(kvp.Key, ConditionOperator.Null);
                else if (kvp.Value.Contains('%'))
                    query.Criteria.AddCondition(kvp.Key, ConditionOperator.Like, kvp.Value);
                else
                    query.Criteria.AddCondition(kvp.Key, ConditionOperator.Equal, kvp.Value);
            }
        }

        return query;
    }

    private static QueryExpression BuildQueryExpression(string entityName, List<CrmSearchFilter> searchParameters, LogicalSearchOperator searchOperator, List<string> fieldList)
    {
        var query = new QueryExpression(entityName);
        query.NoLock = true;
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

    private static Dictionary<string, Dictionary<string, object>>? EntityCollectionToDictionary(EntityCollection ec)
    {
        var result = new Dictionary<string, Dictionary<string, object>>();
        foreach (var entity in ec.Entities)
        {
            var dict = EntityToDictionary(entity);
            dict["ReturnProperty_EntityName"] = entity.LogicalName;
            dict["ReturnProperty_Id "] = entity.Id;
            result[Guid.NewGuid().ToString()] = dict;
        }
        if (result.Count == 0)
            return null;
        return result;
    }

    /// <summary>
    /// Converts an <see cref="Entity"/> to the dictionary format expected by the
    /// original <c>CrmServiceClient</c>. Each attribute produces two entries:
    /// <c>"key"</c> → formatted value (string) or raw SDK value, and
    /// <c>"key_Property"</c> → the raw <see cref="KeyValuePair{TKey,TValue}"/>
    /// with the typed SDK value. CMT's <c>GetDataByKeyFromResultsSet&lt;T&gt;</c>
    /// uses the <c>_Property</c> fallback when the primary value is a string
    /// but a typed value (e.g. Guid) is needed.
    /// </summary>
    private static Dictionary<string, object> EntityToDictionary(Entity entity)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in entity.Attributes)
        {
            // Store _Property entry with the raw typed value (KeyValuePair).
            dict[attr.Key + "_Property"] = attr;
            // Store the display/formatted value if available, otherwise raw value.
            dict[attr.Key] = entity.FormattedValues.ContainsKey(attr.Key)
                ? entity.FormattedValues[attr.Key]
                : attr.Value;
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
            CrmFieldType.Customer or CrmFieldType.Lookup => wrapper.Value is EntityReference er
            ? er
            : new EntityReference(wrapper.ReferencedEntity ?? string.Empty,
                wrapper.Value is Guid g2 ? g2 : Guid.Parse(wrapper.Value.ToString()!)),
            _ => wrapper.Value
        };
    }

    #endregion

    #region Solution import methods
    // CrmPackageCore calls these as instance methods on CrmServiceClient.
    // The modern ServiceClient exposes them as extension methods in
    // Microsoft.PowerPlatform.Dataverse.Client.Extensions, so we delegate.

    /// <summary>
    /// Asynchronously imports a solution to Dataverse from a file path.
    /// Legacy name kept for CrmPackageCore compatibility (it calls ImportSolutionToCrmAsync).
    /// </summary>
    public Guid ImportSolutionToCrmAsync(
        string solutionPath,
        out Guid importId,
        bool activatePlugIns = true,
        bool overwriteUnManagedCustomizations = false,
        bool skipDependancyOnProductUpdateCheckOnInstall = false,
        bool importAsHoldingSolution = false,
        bool isInternalUpgrade = false,
        Dictionary<string, object>? extraParameters = null)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.DeploymentExtensions
            .ImportSolutionAsync(this, solutionPath, out importId, activatePlugIns,
                overwriteUnManagedCustomizations, skipDependancyOnProductUpdateCheckOnInstall,
                importAsHoldingSolution, isInternalUpgrade, extraParameters!);
    }

    /// <summary>
    /// Asynchronously imports a staged solution.
    /// </summary>
    public Guid ImportSolutionAsync(
        Guid stageSolutionUploadId,
        out Guid importId,
        bool activatePlugIns = true,
        bool overwriteUnManagedCustomizations = false,
        bool skipDependancyOnProductUpdateCheckOnInstall = false,
        bool importAsHoldingSolution = false,
        bool isInternalUpgrade = false,
        Dictionary<string, object>? extraParameters = null)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.DeploymentExtensions
            .ImportSolutionAsync(this, stageSolutionUploadId, out importId, activatePlugIns,
                overwriteUnManagedCustomizations, skipDependancyOnProductUpdateCheckOnInstall,
                importAsHoldingSolution, isInternalUpgrade, extraParameters!);
    }

    /// <summary>
    /// Synchronously imports a solution to Dataverse from a file path.
    /// </summary>
    public Guid ImportSolutionToCrm(
        string solutionPath,
        out Guid importId,
        bool activatePlugIns = true,
        bool overwriteUnManagedCustomizations = false,
        bool skipDependancyOnProductUpdateCheckOnInstall = false,
        bool importAsHoldingSolution = false,
        bool isInternalUpgrade = false,
        Dictionary<string, object>? extraParameters = null)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.DeploymentExtensions
            .ImportSolution(this, solutionPath, out importId, activatePlugIns,
                overwriteUnManagedCustomizations, skipDependancyOnProductUpdateCheckOnInstall,
                importAsHoldingSolution, isInternalUpgrade, extraParameters!);
    }

    /// <summary>
    /// Synchronously imports a staged solution.
    /// </summary>
    public Guid ImportSolutionToCrm(
        Guid stageSolutionUploadId,
        out Guid importId,
        bool activatePlugIns = true,
        bool overwriteUnManagedCustomizations = false,
        bool skipDependancyOnProductUpdateCheckOnInstall = false,
        bool importAsHoldingSolution = false,
        bool isInternalUpgrade = false,
        Dictionary<string, object>? extraParameters = null)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.DeploymentExtensions
            .ImportSolution(this, stageSolutionUploadId, out importId, activatePlugIns,
                overwriteUnManagedCustomizations, skipDependancyOnProductUpdateCheckOnInstall,
                importAsHoldingSolution, isInternalUpgrade, extraParameters!);
    }

    #endregion

    #region Batch Operations

    /// <summary>
    /// Creates a batch operation request. Legacy CrmServiceClient exposed this
    /// as an instance method; the modern ServiceClient has it as an extension
    /// method in <see cref="Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions"/>.
    /// </summary>
    public Guid CreateBatchOperationRequest(string batchName, bool returnResults = true, bool continueOnError = false)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
            .CreateBatchOperationRequest(this, batchName, returnResults, continueOnError);
    }

    /// <summary>
    /// Executes a batch operation by ID.
    /// </summary>
    public ExecuteMultipleResponse ExecuteBatch(Guid batchId)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
            .ExecuteBatch(this, batchId);
    }

    /// <summary>
    /// Returns a request batch by BatchID, wrapped in the legacy
    /// <see cref="RequestBatch"/> shim type.
    /// </summary>
    public RequestBatch? GetBatchById(Guid batchId)
    {
        var modern = Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
            .GetBatchById(this, batchId);
        return modern is not null ? new RequestBatch(modern) : null;
    }

    /// <summary>
    /// Returns the batch ID for a given batch name.
    /// </summary>
    public Guid GetBatchOperationIdRequestByName(string batchName)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
            .GetBatchOperationIdRequestByName(this, batchName);
    }

    /// <summary>
    /// Releases a batch from the stack once processing is complete.
    /// </summary>
    public void ReleaseBatchInfoById(Guid batchId)
    {
        Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
            .ReleaseBatchInfoById(this, batchId);
    }

    /// <summary>
    /// Retrieves the response from a batch operation.
    /// </summary>
    public object? RetrieveBatchResponse(Guid batchId)
    {
        try
        {
            return Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
                .RetrieveBatchResponse(this, batchId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the organization request at a given position within a batch.
    /// </summary>
    public OrganizationRequest? GetBatchRequestAtPosition(Guid batchId, int position)
    {
        return Microsoft.PowerPlatform.Dataverse.Client.Extensions.BatchExtensions
            .GetBatchRequestAtPosition(this, batchId, position);
    }

    #endregion
}
