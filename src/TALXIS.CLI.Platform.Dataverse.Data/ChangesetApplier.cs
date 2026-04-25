using System.Diagnostics;
using System.ServiceModel;
using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Applies changeset operations against a live Dataverse environment.
/// Follows a 4-phase pipeline: schema → publish → data → summary.
/// </summary>
internal sealed class ChangesetApplier : IChangesetApplier
{
    public async Task<ChangesetApplyResult> ApplyAsync(
        string? profileName,
        IReadOnlyList<StagedOperation> operations,
        string strategy,
        bool continueOnError,
        CancellationToken ct)
    {
        var schemaOps = operations.Where(o => o.Category == "schema").ToList();
        var dataOps = operations.Where(o => o.Category == "data").ToList();
        var results = new List<OperationResult>();
        var stopwatch = Stopwatch.StartNew();
        int skipped = 0;

        // Phase 1: Schema operations (always sequential via existing service methods)
        if (schemaOps.Count > 0)
        {
            OutputWriter.WriteLine($"Schema: applying {schemaOps.Count} operations...");
            var schemaResults = await ApplySchemaOperationsAsync(profileName, schemaOps, ct).ConfigureAwait(false);
            results.AddRange(schemaResults);

            // If schema had failures and not continue-on-error, skip data phase
            if (schemaResults.Any(r => !r.Success) && !continueOnError)
            {
                skipped = dataOps.Count;
                foreach (var op in dataOps)
                {
                    results.Add(new OperationResult(op.Index, false, $"SKIPPED {op.OperationType} {op.TargetType} {op.TargetDescription}", "Skipped due to schema failures"));
                }
                dataOps = new List<StagedOperation>();
            }
        }

        // Separate file uploads (chunked API, cannot be batched) from regular data ops
        var fileUploadOps = dataOps.Where(o => o.TargetType == "file" && o.OperationType == "UPLOAD").ToList();
        var batchableDataOps = dataOps.Except(fileUploadOps).ToList();

        // Phase 2: Data operations (strategy-dependent)
        if (batchableDataOps.Count > 0)
        {
            OutputWriter.WriteLine($"Data: applying {batchableDataOps.Count} operations with '{strategy}' strategy...");
            var dataResults = strategy switch
            {
                "batch" => await ApplyDataBatchAsync(profileName, batchableDataOps, continueOnError, ct).ConfigureAwait(false),
                "transaction" => await ApplyDataTransactionAsync(profileName, batchableDataOps, ct).ConfigureAwait(false),
                "bulk" => await ApplyDataBulkAsync(profileName, batchableDataOps, continueOnError, ct).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown strategy: {strategy}")
            };
            results.AddRange(dataResults);
        }

        // Phase 3: File uploads (sequential — uses chunked block API, not batchable)
        if (fileUploadOps.Count > 0)
        {
            OutputWriter.WriteLine($"Files: uploading {fileUploadOps.Count} file(s) sequentially...");
            var fileResults = await ApplyFileUploadsAsync(profileName, fileUploadOps, continueOnError, ct).ConfigureAwait(false);
            results.AddRange(fileResults);
        }

        stopwatch.Stop();

        int succeeded = results.Count(r => r.Success);
        int failed = results.Count(r => !r.Success) - skipped;

        return new ChangesetApplyResult
        {
            TotalOperations = operations.Count,
            Succeeded = succeeded,
            Failed = failed,
            Skipped = skipped,
            RolledBack = 0,
            Duration = stopwatch.Elapsed,
            Results = results
        };
    }

    /// <summary>
    /// Applies schema operations using batched APIs where possible.
    /// Phase 1a: Batch-creates new entities + their simple attributes via ExecuteMultiple
    ///           (one CreateEntityRequest per entity, then CreateAttributeRequest per attribute).
    /// Phase 1b: Remaining operations dispatch individually.
    /// Phase 2: Single PublishXml for all affected entities.
    /// </summary>
    private async Task<List<OperationResult>> ApplySchemaOperationsAsync(
        string? profileName, IReadOnlyList<StagedOperation> ops, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var results = new List<OperationResult>();
        var affectedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var metadataService = TxcServices.Get<IDataverseEntityMetadataService>();
        var optionSetService = TxcServices.Get<IDataverseOptionSetService>();

        // Identify new-entity ops and their associated inline-eligible attribute ops
        var newEntityOps = ops.Where(o => o.TargetType == "entity" && o.OperationType == "CREATE").ToList();
        var newEntityNames = newEntityOps
            .Select(o => (GetParamOrDefault<string?>(o, "schemaName") ?? GetParamOrDefault<string?>(o, "name"))?.ToLowerInvariant())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Only simple attribute types can be batched; complex types (choice, multichoice, lookup) fall through to Phase 1b
        var batchableAttrTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "string", "number", "money", "bool", "datetime", "decimal", "float", "file", "image" };

        var newEntityAttrOps = ops.Where(o =>
            o.TargetType == "attribute" && o.OperationType == "CREATE"
            && batchableAttrTypes.Contains(GetParamOrDefault<string?>(o, "type")?.ToLowerInvariant() ?? "")
            && newEntityNames.Contains(GetParamOrDefault<string?>(o, "entity")?.ToLowerInvariant())).ToList();

        var remainingOps = ops.Except(newEntityOps).Except(newEntityAttrOps).ToList();

        // Phase 1a: Batch-create new entities + their attributes via ExecuteMultiple
        if (newEntityOps.Count > 0)
        {
            try
            {
                var batchRequests = new OrganizationRequestCollection();
                // Track which staged operations map to which batch request index
                var batchOpMap = new List<StagedOperation>();

                foreach (var entityOp in newEntityOps)
                {
                    var entityName = GetParamOrDefault<string?>(entityOp, "schemaName")
                                  ?? GetParam<string>(entityOp, "name");
                    var displayName = GetParamOrDefault<string?>(entityOp, "displayName") ?? entityName;
                    var pluralName = GetParamOrDefault<string?>(entityOp, "pluralName") ?? displayName + "s";
                    var description = GetParamOrDefault<string?>(entityOp, "description");
                    var solution = GetParamOrDefault<string?>(entityOp, "solution");
                    var ownership = GetParamOrDefault<string?>(entityOp, "ownership") ?? "user";
                    var tableType = GetParamOrDefault<string?>(entityOp, "tableType") ?? "standard";
                    var hasNotes = GetParamOrDefault<bool>(entityOp, "hasNotes");
                    var hasActivities = GetParamOrDefault<bool>(entityOp, "hasActivities");
                    var enableAudit = GetParamOrDefault<bool>(entityOp, "enableAudit");
                    var enableChangeTracking = GetParamOrDefault<bool>(entityOp, "enableChangeTracking");

                    var entityMeta = new EntityMetadata
                    {
                        SchemaName = entityName,
                        DisplayName = new Label(displayName, 1033),
                        DisplayCollectionName = new Label(pluralName, 1033),
                        OwnershipType = ownership.ToLowerInvariant() switch
                        {
                            "organization" or "org" => OwnershipTypes.OrganizationOwned,
                            _ => OwnershipTypes.UserOwned
                        },
                        IsActivity = tableType.ToLowerInvariant() == "activity",
                        HasNotes = hasNotes,
                        HasActivities = hasActivities,
                        IsAuditEnabled = new BooleanManagedProperty(enableAudit),
                        ChangeTrackingEnabled = enableChangeTracking
                    };

                    if (tableType.ToLowerInvariant() == "elastic")
                        entityMeta.TableType = "Elastic";

                    if (description is not null)
                        entityMeta.Description = new Label(description, 1033);

                    // Primary name attribute (required when creating an entity)
                    var prefix = entityName.Contains('_') ? entityName[..entityName.IndexOf('_')] : entityName;
                    var primaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = $"{prefix}_name",
                        MaxLength = 200,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                        DisplayName = new Label("Name", 1033)
                    };

                    var createRequest = new CreateEntityRequest
                    {
                        Entity = entityMeta,
                        PrimaryAttribute = primaryAttribute
                    };

                    if (!string.IsNullOrEmpty(solution))
                        createRequest["SolutionUniqueName"] = solution;

                    batchRequests.Add(createRequest);
                    batchOpMap.Add(entityOp);
                    affectedEntities.Add(entityName.ToLowerInvariant());
                }

                // Add CreateAttributeRequest for each batched attribute
                foreach (var attrOp in newEntityAttrOps)
                {
                    var entityLogicalName = GetParam<string>(attrOp, "entity");
                    var attrMeta = BuildAttributeMetadataFromStagedOp(attrOp);
                    if (attrMeta is null) continue;

                    var attrRequest = new CreateAttributeRequest
                    {
                        EntityName = entityLogicalName,
                        Attribute = attrMeta
                    };

                    var solution = GetParamOrDefault<string?>(attrOp, "solution");
                    if (!string.IsNullOrEmpty(solution))
                        attrRequest["SolutionUniqueName"] = solution;

                    batchRequests.Add(attrRequest);
                    batchOpMap.Add(attrOp);
                }

                // Execute all entity + attribute creates in a single round-trip
                var stopwatch = Stopwatch.StartNew();
                var response = (ExecuteMultipleResponse)await conn.Client.ExecuteAsync(
                    new ExecuteMultipleRequest
                    {
                        Settings = new ExecuteMultipleSettings
                        {
                            ContinueOnError = false,
                            ReturnResponses = true
                        },
                        Requests = batchRequests
                    }, ct).ConfigureAwait(false);
                stopwatch.Stop();

                // Process batch results
                var faultedIndices = new HashSet<int>();
                foreach (var item in response.Responses)
                {
                    if (item.Fault is not null)
                        faultedIndices.Add(item.RequestIndex);
                }

                if (faultedIndices.Count == 0)
                {
                    // All succeeded
                    foreach (var op in newEntityOps)
                        results.Add(new OperationResult(op.Index, true, $"CREATE entity {op.TargetDescription} (batched)"));
                    foreach (var op in newEntityAttrOps)
                        results.Add(new OperationResult(op.Index, true, $"CREATE attribute {op.TargetDescription} (batched with entity)"));

                    OutputWriter.WriteLine($"Created {newEntityOps.Count} entities with {newEntityAttrOps.Count} attributes via ExecuteMultiple in {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    // Partial failure — report per-operation results and fall back for the rest
                    var handledOps = new HashSet<StagedOperation>();
                    for (int i = 0; i < batchOpMap.Count; i++)
                    {
                        var op = batchOpMap[i];
                        if (faultedIndices.Contains(i))
                        {
                            var fault = response.Responses.First(r => r.RequestIndex == i).Fault;
                            results.Add(new OperationResult(op.Index, false,
                                $"{op.OperationType} {op.TargetType} {op.TargetDescription}", fault?.Message));
                        }
                        else
                        {
                            results.Add(new OperationResult(op.Index, true,
                                $"{op.OperationType} {op.TargetType} {op.TargetDescription} (batched)"));
                        }
                        handledOps.Add(op);
                    }

                    // Remove handled ops from remaining so they aren't processed again
                    remainingOps = ops.Except(handledOps).ToList();
                }
            }
            catch (Exception ex)
            {
                // Fallback: if batch fails entirely, process all individually
                OutputWriter.WriteLine($"Batched schema creation failed: {ex.Message}. Falling back to individual creates.");
                remainingOps = ops.ToList();
                results.Clear();
                affectedEntities.Clear();
            }
        }

        // Phase 1b: Process remaining schema operations individually
        foreach (var op in remainingOps)
        {
            try
            {
                await DispatchSchemaOperationAsync(profileName, op, metadataService, optionSetService, ct).ConfigureAwait(false);

                var entityName = GetEntityName(op);
                if (entityName is not null)
                    affectedEntities.Add(entityName);

                results.Add(new OperationResult(op.Index, true, $"{op.OperationType} {op.TargetType} {op.TargetDescription}"));
            }
            catch (Exception ex)
            {
                results.Add(new OperationResult(op.Index, false, $"{op.OperationType} {op.TargetType} {op.TargetDescription}", ex.Message));
            }
        }

        // Phase 2: Single PublishXml for all affected entities
        if (affectedEntities.Count > 0)
        {
            try
            {
                var entitiesXml = string.Join("", affectedEntities.Select(e => $"<entity>{System.Security.SecurityElement.Escape(e)}</entity>"));
                var publishXml = $"<importexportxml><entities>{entitiesXml}</entities></importexportxml>";
                await conn.Client.ExecuteAsync(new PublishXmlRequest
                {
                    ParameterXml = publishXml
                }, ct).ConfigureAwait(false);
                OutputWriter.WriteLine($"Published {affectedEntities.Count} entities in single PublishXml call");
            }
            catch (Exception ex)
            {
                OutputWriter.WriteLine($"Publish failed: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Dispatches a single schema operation to the appropriate service method.
    /// </summary>
    private static async Task DispatchSchemaOperationAsync(
        string? profileName,
        StagedOperation op,
        IDataverseEntityMetadataService metadataService,
        IDataverseOptionSetService optionSetService,
        CancellationToken ct)
    {
        switch (op.TargetType, op.OperationType)
        {
            case ("entity", "CREATE"):
                // Staging stores "name"; also accept "schemaName"
                var entitySchemaName = GetParamOrDefault<string?>(op, "schemaName")
                                    ?? GetParam<string>(op, "name");
                await metadataService.CreateEntityAsync(
                    profileName,
                    new CreateEntityOptions
                    {
                        SchemaName = entitySchemaName,
                        DisplayName = GetParam<string>(op, "displayName"),
                        PluralName = GetParamOrDefault<string?>(op, "pluralName") ?? GetParam<string>(op, "displayName") + "s",
                        Description = GetParamOrDefault<string?>(op, "description"),
                        Solution = GetParamOrDefault<string?>(op, "solution"),
                        Ownership = GetParamOrDefault<string?>(op, "ownership") ?? "user",
                        TableType = GetParamOrDefault<string?>(op, "tableType") ?? "standard",
                        HasNotes = GetParamOrDefault<bool>(op, "hasNotes"),
                        HasActivities = GetParamOrDefault<bool>(op, "hasActivities"),
                        EnableAudit = GetParamOrDefault<bool>(op, "enableAudit"),
                        EnableChangeTracking = GetParamOrDefault<bool>(op, "enableChangeTracking")
                    },
                    ct).ConfigureAwait(false);
                break;

            case ("entity", "UPDATE"):
                // Staging stores "entity"; also accept "name"
                var entityUpdateName = GetParamOrDefault<string?>(op, "name")
                                    ?? GetParam<string>(op, "entity");
                await metadataService.UpdateEntityAsync(
                    profileName,
                    entityUpdateName,
                    GetParamOrDefault<string?>(op, "displayName"),
                    GetParamOrDefault<string?>(op, "pluralName"),
                    GetParamOrDefault<string?>(op, "description"),
                    ct).ConfigureAwait(false);
                break;

            case ("entity", "DELETE"):
                // Staging stores "entity"; also accept "name"
                var entityDeleteName = GetParamOrDefault<string?>(op, "name")
                                    ?? GetParam<string>(op, "entity");
                await metadataService.DeleteEntityAsync(
                    profileName,
                    entityDeleteName,
                    ct).ConfigureAwait(false);
                break;

            case ("attribute", "CREATE"):
                await metadataService.CreateAttributeAsync(
                    profileName,
                    BuildCreateAttributeOptions(op),
                    ct).ConfigureAwait(false);
                break;

            case ("attribute", "UPDATE"):
                // Staging stores "required"; also accept "requiredLevel"
                var requiredLevel = GetParamOrDefault<string?>(op, "requiredLevel")
                                 ?? GetParamOrDefault<string?>(op, "required");
                await metadataService.UpdateAttributeAsync(
                    profileName,
                    GetParam<string>(op, "entity"),
                    GetParam<string>(op, "name"),
                    GetParamOrDefault<string?>(op, "displayName"),
                    GetParamOrDefault<string?>(op, "description"),
                    requiredLevel,
                    ct).ConfigureAwait(false);
                break;

            case ("attribute", "DELETE"):
                await metadataService.DeleteAttributeAsync(
                    profileName,
                    GetParam<string>(op, "entity"),
                    GetParam<string>(op, "name"),
                    ct).ConfigureAwait(false);
                break;

            case ("relationship", "CREATE"):
                // Staging stores "name"; also accept "schemaName"
                var relCreateName = GetParamOrDefault<string?>(op, "schemaName")
                                 ?? GetParam<string>(op, "name");
                await metadataService.CreateManyToManyRelationshipAsync(
                    profileName,
                    GetParam<string>(op, "entity1"),
                    GetParam<string>(op, "entity2"),
                    relCreateName,
                    GetParamOrDefault<string?>(op, "displayName"),
                    ct).ConfigureAwait(false);
                break;

            case ("relationship", "DELETE"):
                // Staging stores "name"; also accept "schemaName"
                var relDeleteName = GetParamOrDefault<string?>(op, "schemaName")
                                 ?? GetParam<string>(op, "name");
                await metadataService.DeleteRelationshipAsync(
                    profileName,
                    relDeleteName,
                    ct).ConfigureAwait(false);
                break;

            case ("optionset", "CREATE"):
                // Staging stores "name"; also accept "schemaName"
                var optSetName = GetParamOrDefault<string?>(op, "schemaName")
                              ?? GetParam<string>(op, "name");
                var options = GetOptionsArray(op);
                await optionSetService.CreateGlobalOptionSetAsync(
                    profileName,
                    optSetName,
                    GetParam<string>(op, "displayName"),
                    GetParamOrDefault<string?>(op, "description"),
                    options,
                    GetParamOrDefault<string?>(op, "solution"),
                    ct).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException(
                    $"Schema operation {op.TargetType}/{op.OperationType} is not supported.");
        }
    }

    // ── Data strategy: Batch (ExecuteMultiple) ──────────────────────────

    private async Task<List<OperationResult>> ApplyDataBatchAsync(
        string? profileName, IReadOnlyList<StagedOperation> ops, bool continueOnError, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var requests = new OrganizationRequestCollection();
        foreach (var op in ops)
        {
            requests.Add(BuildOrganizationRequest(op));
        }

        var response = (ExecuteMultipleResponse)await conn.Client.ExecuteAsync(
            new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = continueOnError,
                    ReturnResponses = true
                },
                Requests = requests
            }, ct).ConfigureAwait(false);

        var results = new List<OperationResult>();

        // Build a set of indices that have explicit responses (success or fault)
        var respondedIndices = new HashSet<int>();
        foreach (var item in response.Responses)
        {
            respondedIndices.Add(item.RequestIndex);
            var op = ops[item.RequestIndex];
            if (item.Fault is not null)
            {
                results.Add(new OperationResult(op.Index, false,
                    $"{op.OperationType} {op.TargetType} {op.TargetDescription}",
                    item.Fault.Message));
            }
            else
            {
                results.Add(new OperationResult(op.Index, true,
                    $"{op.OperationType} {op.TargetType} {op.TargetDescription}"));
            }
        }

        // When ContinueOnError=false, mark operations after the faulted index as not executed
        if (!continueOnError)
        {
            var faultedIndex = response.Responses.FirstOrDefault(r => r.Fault != null)?.RequestIndex;
            if (faultedIndex.HasValue)
            {
                for (int i = faultedIndex.Value + 1; i < ops.Count; i++)
                {
                    if (!respondedIndices.Contains(i))
                    {
                        var op = ops[i];
                        results.Add(new OperationResult(op.Index, false,
                            $"{op.OperationType} {op.TargetType} {op.TargetDescription}",
                            "Not executed (batch aborted after earlier failure)"));
                        respondedIndices.Add(i);
                    }
                }
            }
        }

        // Any operation not in the response set succeeded silently
        for (int i = 0; i < ops.Count; i++)
        {
            if (!respondedIndices.Contains(i))
            {
                var op = ops[i];
                results.Add(new OperationResult(op.Index, true,
                    $"{op.OperationType} {op.TargetType} {op.TargetDescription}"));
            }
        }

        return results;
    }

    // ── Data strategy: Transaction (ExecuteTransaction) ─────────────────

    private async Task<List<OperationResult>> ApplyDataTransactionAsync(
        string? profileName, IReadOnlyList<StagedOperation> ops, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var requests = new OrganizationRequestCollection();
        foreach (var op in ops)
        {
            requests.Add(BuildOrganizationRequest(op));
        }

        try
        {
            var response = (ExecuteTransactionResponse)await conn.Client.ExecuteAsync(
                new ExecuteTransactionRequest
                {
                    Requests = requests,
                    ReturnResponses = true
                }, ct).ConfigureAwait(false);

            // All operations succeeded
            return ops.Select(op => new OperationResult(op.Index, true,
                $"{op.OperationType} {op.TargetType} {op.TargetDescription}")).ToList();
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            // Transaction rolled back — identify the faulted request
            int faultedIndex = -1;
            if (ex.Detail is ExecuteTransactionFault txFault)
            {
                faultedIndex = txFault.FaultedRequestIndex;
            }

            var results = new List<OperationResult>();
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (i == faultedIndex)
                {
                    results.Add(new OperationResult(op.Index, false,
                        $"{op.OperationType} {op.TargetType} {op.TargetDescription}",
                        ex.Detail.Message));
                }
                else
                {
                    results.Add(new OperationResult(op.Index, false,
                        $"{op.OperationType} {op.TargetType} {op.TargetDescription}",
                        "Rolled back"));
                }
            }

            // Update the result to reflect rollback
            return results;
        }
    }

    // ── Data strategy: Bulk (CreateMultiple / UpdateMultiple) ────────────

    private async Task<List<OperationResult>> ApplyDataBulkAsync(
        string? profileName, IReadOnlyList<StagedOperation> ops, bool continueOnError, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var results = new List<OperationResult>();

        // Group operations by (entity, operation type) for bulk messages
        var groups = ops.GroupBy(o => (
            Entity: o.Parameters.GetValueOrDefault("entity")?.ToString(),
            OpType: o.OperationType));

        foreach (var group in groups)
        {
            var entityName = group.Key.Entity;
            var opType = group.Key.OpType;
            var groupOps = group.ToList();

            if (string.IsNullOrEmpty(entityName))
            {
                foreach (var op in groupOps)
                    results.Add(new OperationResult(op.Index, false,
                        $"{op.OperationType} {op.TargetType} {op.TargetDescription}",
                        "Missing 'entity' parameter for bulk operation"));
                continue;
            }

            try
            {
                if (opType == "CREATE")
                {
                    var entities = new EntityCollection { EntityName = entityName };
                    foreach (var op in groupOps)
                        entities.Entities.Add(BuildEntity(op));

                    await conn.Client.ExecuteAsync(
                        new CreateMultipleRequest { Targets = entities }, ct).ConfigureAwait(false);

                    foreach (var op in groupOps)
                        results.Add(new OperationResult(op.Index, true,
                            $"{op.OperationType} {op.TargetType} {op.TargetDescription}"));
                }
                else if (opType == "UPDATE")
                {
                    var entities = new EntityCollection { EntityName = entityName };
                    foreach (var op in groupOps)
                        entities.Entities.Add(BuildEntity(op));

                    await conn.Client.ExecuteAsync(
                        new UpdateMultipleRequest { Targets = entities }, ct).ConfigureAwait(false);

                    foreach (var op in groupOps)
                        results.Add(new OperationResult(op.Index, true,
                            $"{op.OperationType} {op.TargetType} {op.TargetDescription}"));
                }
                else
                {
                    // DELETE and other operations are not supported in bulk mode — fall back to batch
                    foreach (var op in groupOps)
                    {
                        try
                        {
                            await conn.Client.ExecuteAsync(BuildOrganizationRequest(op), ct).ConfigureAwait(false);
                            results.Add(new OperationResult(op.Index, true,
                                $"{op.OperationType} {op.TargetType} {op.TargetDescription}"));
                        }
                        catch (Exception ex)
                        {
                            results.Add(new OperationResult(op.Index, false,
                                $"{op.OperationType} {op.TargetType} {op.TargetDescription}", ex.Message));
                            if (!continueOnError) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Entire bulk batch failed — mark all operations in the group as failed
                foreach (var op in groupOps)
                    results.Add(new OperationResult(op.Index, false,
                        $"{op.OperationType} {op.TargetType} {op.TargetDescription}", ex.Message));
                if (!continueOnError) break;
            }
        }

        return results;
    }

    // ── Data strategy: File uploads (sequential, chunked API) ─────────

    private static async Task<List<OperationResult>> ApplyFileUploadsAsync(
        string? profileName, IReadOnlyList<StagedOperation> ops, bool continueOnError, CancellationToken ct)
    {
        var fileService = TxcServices.Get<IDataverseFileService>();
        var results = new List<OperationResult>();

        foreach (var op in ops)
        {
            try
            {
                var entity = op.Parameters["entity"]!.ToString()!;
                var recordId = Guid.Parse(op.Parameters["recordId"]!.ToString()!);
                var column = op.Parameters["column"]!.ToString()!;
                var filePath = op.Parameters["file"]!.ToString()!;

                await fileService.UploadFileAsync(profileName, entity, recordId, column, filePath, ct)
                    .ConfigureAwait(false);

                results.Add(new OperationResult(op.Index, true,
                    $"UPLOAD file {op.TargetDescription}"));
            }
            catch (Exception ex)
            {
                results.Add(new OperationResult(op.Index, false,
                    $"UPLOAD file {op.TargetDescription}", ex.Message));
                if (!continueOnError) break;
            }
        }

        return results;
    }

    // ── CreateEntities helpers ──────────────────────────────────────────

    /// <summary>
    /// Converts a staged attribute CREATE operation into an <see cref="AttributeMetadata"/>
    /// for inline creation with <c>CreateEntities</c>. Only simple types are supported;
    /// complex types (choice, multichoice, lookup) return <c>null</c>.
    /// </summary>
    private static AttributeMetadata? BuildAttributeMetadataFromStagedOp(StagedOperation op)
    {
        // Staging stores "name"; also accept "schemaName"
        var name = GetParamOrDefault<string?>(op, "schemaName")
                ?? GetParamOrDefault<string?>(op, "name");
        var type = GetParamOrDefault<string?>(op, "type")?.ToLowerInvariant();
        var displayName = GetParamOrDefault<string?>(op, "displayName") ?? name;

        if (name is null || type is null) return null;

        AttributeMetadata? attr = type switch
        {
            "string" => new StringAttributeMetadata
            {
                MaxLength = GetParamOrDefault<int?>(op, "maxLength") ?? 200,
            },
            "number" => new IntegerAttributeMetadata(),
            "money" => new MoneyAttributeMetadata(),
            "bool" => new BooleanAttributeMetadata
            {
                OptionSet = new BooleanOptionSetMetadata(
                    new OptionMetadata(new Label(GetParamOrDefault<string?>(op, "trueLabel") ?? "Yes", 1033), 1),
                    new OptionMetadata(new Label(GetParamOrDefault<string?>(op, "falseLabel") ?? "No", 1033), 0))
            },
            "datetime" => new DateTimeAttributeMetadata { Format = DateTimeFormat.DateAndTime },
            "decimal" => new DecimalAttributeMetadata(),
            "float" => new DoubleAttributeMetadata(),
            "file" => new FileAttributeMetadata { MaxSizeInKB = GetParamOrDefault<int?>(op, "maxSizeKb") ?? 131072 },
            "image" => new ImageAttributeMetadata(),
            _ => null
        };

        if (attr is null) return null;

        attr.SchemaName = name;
        attr.DisplayName = new Label(displayName, 1033);

        var requiredLevel = GetParamOrDefault<string?>(op, "requiredLevel")?.ToLowerInvariant() switch
        {
            "required" or "applicationrequired" => AttributeRequiredLevel.ApplicationRequired,
            "recommended" => AttributeRequiredLevel.Recommended,
            _ => AttributeRequiredLevel.None
        };
        attr.RequiredLevel = new AttributeRequiredLevelManagedProperty(requiredLevel);

        return attr;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a staged data operation into the corresponding Dataverse SDK request.
    /// File uploads return <c>null</c> — they use the chunked block API and cannot be batched.
    /// </summary>
    private static OrganizationRequest? BuildOrganizationRequest(StagedOperation op)
    {
        return (op.TargetType, op.OperationType) switch
        {
            ("record", "CREATE") => new CreateRequest { Target = BuildEntity(op) },
            ("record", "UPDATE") => new UpdateRequest { Target = BuildEntity(op) },
            ("record", "DELETE") => new DeleteRequest
            {
                Target = new EntityReference(
                    op.Parameters["entity"]!.ToString()!,
                    Guid.Parse(op.Parameters["recordId"]!.ToString()!))
            },
            ("record", "ASSOCIATE") => new AssociateRequest
            {
                Target = new EntityReference(
                    op.Parameters["entity"]!.ToString()!,
                    Guid.Parse(op.Parameters["recordId"]!.ToString()!)),
                RelatedEntities = new EntityReferenceCollection
                {
                    new EntityReference(
                        op.Parameters["targetEntity"]!.ToString()!,
                        Guid.Parse(op.Parameters["target"]!.ToString()!))
                },
                Relationship = new Relationship(op.Parameters["relationship"]!.ToString()!)
            },
            ("record", "DISASSOCIATE") => new DisassociateRequest
            {
                Target = new EntityReference(
                    op.Parameters["entity"]!.ToString()!,
                    Guid.Parse(op.Parameters["recordId"]!.ToString()!)),
                RelatedEntities = new EntityReferenceCollection
                {
                    new EntityReference(
                        op.Parameters["targetEntity"]!.ToString()!,
                        Guid.Parse(op.Parameters["target"]!.ToString()!))
                },
                Relationship = new Relationship(op.Parameters["relationship"]!.ToString()!)
            },
            ("file", "UPLOAD") => null, // File uploads use chunked API — handled separately
            _ => throw new NotSupportedException(
                $"Data operation {op.TargetType}/{op.OperationType} is not supported in batch mode.")
        };
    }

    /// <summary>
    /// Builds a Dataverse <see cref="Entity"/> from a staged record operation's parameters.
    /// </summary>
    private static Entity BuildEntity(StagedOperation op)
    {
        var entityName = op.Parameters["entity"]!.ToString()!;

        // The "attributes" (or "data") parameter holds either a JsonElement or a serialized JSON string
        JsonElement attributesJson;
        var rawAttributes = op.Parameters.GetValueOrDefault("attributes")
                         ?? op.Parameters.GetValueOrDefault("data");
        if (rawAttributes is JsonElement je)
        {
            attributesJson = je;
        }
        else if (rawAttributes is string jsonStr)
        {
            attributesJson = JsonSerializer.Deserialize<JsonElement>(jsonStr);
        }
        else
        {
            throw new InvalidOperationException(
                $"Operation #{op.Index} is missing the 'attributes' parameter required to build an entity.");
        }

        Guid? recordId = null;
        if (op.Parameters.TryGetValue("recordId", out var idObj) && idObj is not null)
        {
            recordId = Guid.Parse(idObj.ToString()!);
        }

        return EntityJsonConverter.JsonToEntity(entityName, attributesJson, recordId);
    }

    /// <summary>
    /// Extracts the entity logical name from a staged operation for publish tracking.
    /// </summary>
    private static string? GetEntityName(StagedOperation op)
    {
        if (op.Parameters.TryGetValue("entity", out var e) && e is not null)
            return e.ToString();
        if (op.Parameters.TryGetValue("name", out var n) && n is not null)
            return n.ToString();
        if (op.Parameters.TryGetValue("entityLogicalName", out var eln) && eln is not null)
            return eln.ToString();
        return null;
    }

    /// <summary>
    /// Gets a required parameter value from a staged operation.
    /// </summary>
    private static T GetParam<T>(StagedOperation op, string key)
    {
        if (!op.Parameters.TryGetValue(key, out var value) || value is null)
            throw new InvalidOperationException($"Operation #{op.Index} is missing required parameter '{key}'.");

        if (value is T typed)
            return typed;

        // Handle JsonElement conversion
        if (value is JsonElement je)
            return JsonSerializer.Deserialize<T>(je.GetRawText())!;

        // Fall back to string conversion
        return (T)Convert.ChangeType(value.ToString()!, typeof(T));
    }

    /// <summary>
    /// Gets an optional parameter value from a staged operation, returning default if missing.
    /// </summary>
    private static T? GetParamOrDefault<T>(StagedOperation op, string key)
    {
        if (!op.Parameters.TryGetValue(key, out var value) || value is null)
            return default;

        if (value is T typed)
            return typed;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null)
                return default;
            return JsonSerializer.Deserialize<T>(je.GetRawText());
        }

        var str = value.ToString();
        if (string.IsNullOrEmpty(str))
            return default;

        return (T)Convert.ChangeType(str, typeof(T))!;
    }

    /// <summary>
    /// Builds <see cref="CreateAttributeOptions"/> from a staged attribute CREATE operation.
    /// </summary>
    private static CreateAttributeOptions BuildCreateAttributeOptions(StagedOperation op)
    {
        return new CreateAttributeOptions
        {
            EntityLogicalName = GetParam<string>(op, "entity"),
            // Staging stores "name"; also accept "schemaName"
            SchemaName = GetParamOrDefault<string?>(op, "schemaName") ?? GetParam<string>(op, "name"),
            Type = NormalizeAttributeType(GetParam<string>(op, "type")),
            DisplayName = GetParamOrDefault<string?>(op, "displayName"),
            Description = GetParamOrDefault<string?>(op, "description"),
            RequiredLevel = (GetParamOrDefault<string?>(op, "requiredLevel") ?? "none").ToLowerInvariant(),
            SolutionUniqueName = GetParamOrDefault<string?>(op, "solution"),
            MaxLength = GetParamOrDefault<int?>(op, "maxLength"),
            StringFormat = GetParamOrDefault<string?>(op, "stringFormat"),
            MinValue = GetParamOrDefault<double?>(op, "minValue"),
            MaxValue = GetParamOrDefault<double?>(op, "maxValue"),
            Precision = GetParamOrDefault<int?>(op, "precision"),
            NumberFormat = GetParamOrDefault<string?>(op, "numberFormat"),
            PrecisionSource = GetPrecisionSource(op),
            TrueLabel = GetParamOrDefault<string?>(op, "trueLabel") ?? "Yes",
            FalseLabel = GetParamOrDefault<string?>(op, "falseLabel") ?? "No",
            DateTimeFormat = GetParamOrDefault<string?>(op, "dateTimeFormat"),
            DateTimeBehavior = GetParamOrDefault<string?>(op, "dateTimeBehavior"),
            Options = GetOptionTuples(op),
            GlobalOptionSetName = GetParamOrDefault<string?>(op, "globalOptionSetName")
                               ?? GetParamOrDefault<string?>(op, "globalOptionSet"),
            TargetEntity = GetParamOrDefault<string?>(op, "targetEntity"),
            TargetEntities = GetTargetEntities(op),
            CascadeDelete = GetParamOrDefault<string?>(op, "cascadeDelete") ?? "removelink",
            MaxSizeKb = GetParamOrDefault<int?>(op, "maxSizeKb"),
            CanStoreFullImage = GetParamOrDefault<bool?>(op, "canStoreFullImage") ?? true,
            IsAuditable = GetParamOrDefault<bool>(op, "isAuditable"),
            IsSearchable = GetParamOrDefault<bool?>(op, "isSearchable") ?? true,
            IsSecured = GetParamOrDefault<bool>(op, "isSecured")
        };
    }

    private static string[]? GetTargetEntities(StagedOperation op)
    {
        if (!op.Parameters.TryGetValue("targetEntities", out var raw) || raw is null)
            return null;

        if (raw is string[] targets)
            return targets;

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
                return JsonSerializer.Deserialize<string[]>(je.GetRawText());
            if (je.ValueKind == JsonValueKind.String)
                raw = je.GetString();
        }

        return raw is string csv && !string.IsNullOrWhiteSpace(csv)
            ? csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;
    }

    private static int? GetPrecisionSource(StagedOperation op)
    {
        if (!op.Parameters.TryGetValue("precisionSource", out var raw) || raw is null)
            return null;

        if (raw is int i)
            return i;

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var jsonInt))
                return jsonInt;
            if (je.ValueKind == JsonValueKind.String)
                raw = je.GetString();
        }

        var value = raw?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out var parsed))
            return parsed;

        return value.ToLowerInvariant() switch
        {
            "attribute" => 0,
            "organization" => 1,
            "currency" => 2,
            _ => null
        };
    }

    private static string NormalizeAttributeType(string type)
        => type.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static (string Label, int Value)[]? GetOptionTuples(StagedOperation op)
    {
        if (!op.Parameters.TryGetValue("options", out var raw) || raw is null)
            return null;

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                return je.EnumerateArray()
                    .Select(e => (
                        e.GetProperty("label").GetString()!,
                        e.GetProperty("value").GetInt32()))
                    .ToArray();
            }

            if (je.ValueKind == JsonValueKind.String)
                raw = je.GetString();
        }

        return raw is string csv && !string.IsNullOrWhiteSpace(csv)
            ? ParseOptionTuplesCsv(csv)
            : null;
    }

    private static (string Label, int Value)[] ParseOptionTuplesCsv(string csv)
    {
        var entries = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new (string Label, int Value)[entries.Length];
        int autoValue = 100_000_000;

        for (int i = 0; i < entries.Length; i++)
        {
            var parts = entries[i].Split(':', 2, StringSplitOptions.TrimEntries);
            var value = parts.Length == 2 && int.TryParse(parts[1], out int parsed) ? parsed : autoValue++;
            if (parts.Length < 2) autoValue = value + 1;
            results[i] = (parts[0], value);
        }

        return results;
    }

    /// <summary>
    /// Extracts an array of <see cref="OptionMetadataInput"/> from a staged optionset CREATE operation.
    /// Supports three formats:
    /// 1. JSON array of {label, value} objects (from deserialized changeset files)
    /// 2. CSV string like "Label1:100000000,Label2:100000001" or "Label1,Label2" (from staging commands)
    /// 3. Already-deserialized OptionMetadataInput[]
    /// </summary>
    private static OptionMetadataInput[] GetOptionsArray(StagedOperation op)
    {
        if (!op.Parameters.TryGetValue("options", out var raw) || raw is null)
            return Array.Empty<OptionMetadataInput>();

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                return je.EnumerateArray()
                    .Select(e => new OptionMetadataInput(
                        e.GetProperty("label").GetString()!,
                        e.GetProperty("value").GetInt32()))
                    .ToArray();
            }

            // JSON string value — parse as CSV below
            if (je.ValueKind == JsonValueKind.String)
                raw = je.GetString();
        }

        // If already deserialized, try casting
        if (raw is OptionMetadataInput[] opts)
            return opts;

        // CSV string: "Label1:100000000,Label2:100000001" or "Label1,Label2" (auto-valued)
        if (raw is string csv && !string.IsNullOrWhiteSpace(csv))
            return ParseOptionsCsv(csv);

        return Array.Empty<OptionMetadataInput>();
    }

    /// <summary>
    /// Parses a CSV options string into <see cref="OptionMetadataInput"/> items.
    /// Matches the format used by <c>EntityOptionSetCreateGlobalCliCommand.ParseOptions</c>.
    /// </summary>
    private static OptionMetadataInput[] ParseOptionsCsv(string csv)
    {
        var entries = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
            return Array.Empty<OptionMetadataInput>();

        var results = new OptionMetadataInput[entries.Length];
        int autoValue = 100_000_000;

        for (int i = 0; i < entries.Length; i++)
        {
            var parts = entries[i].Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int v))
            {
                results[i] = new OptionMetadataInput(parts[0].Trim(), v);
            }
            else
            {
                results[i] = new OptionMetadataInput(parts[0].Trim(), autoValue++);
            }
        }

        return results;
    }
}
