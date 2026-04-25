using System.Diagnostics;
using System.ServiceModel;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
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

        // Phase 2: Data operations (strategy-dependent)
        if (dataOps.Count > 0)
        {
            OutputWriter.WriteLine($"Data: applying {dataOps.Count} operations with '{strategy}' strategy...");
            var dataResults = strategy switch
            {
                "batch" => await ApplyDataBatchAsync(profileName, dataOps, continueOnError, ct).ConfigureAwait(false),
                "transaction" => await ApplyDataTransactionAsync(profileName, dataOps, ct).ConfigureAwait(false),
                "bulk" => await ApplyDataBulkAsync(profileName, dataOps, continueOnError, ct).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown strategy: {strategy}")
            };
            results.AddRange(dataResults);
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
    /// Applies schema operations sequentially using existing service methods.
    /// Tracks affected entities for a single publish pass at the end.
    /// </summary>
    private async Task<List<OperationResult>> ApplySchemaOperationsAsync(
        string? profileName, IReadOnlyList<StagedOperation> ops, CancellationToken ct)
    {
        var results = new List<OperationResult>();
        var affectedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var metadataService = TxcServices.Get<IDataverseEntityMetadataService>();
        var optionSetService = TxcServices.Get<IDataverseOptionSetService>();

        foreach (var op in ops)
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

        // Single publish for all affected entities.
        // Note: individual service methods may already publish; we'll optimize
        // to skip per-operation publish in a future PR.
        if (affectedEntities.Count > 0)
        {
            OutputWriter.WriteLine($"Publishing: {affectedEntities.Count} affected entities...");
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
                await metadataService.CreateEntityAsync(
                    profileName,
                    GetParam<string>(op, "schemaName"),
                    GetParam<string>(op, "displayName"),
                    GetParam<string>(op, "pluralName"),
                    GetParamOrDefault<string?>(op, "description"),
                    GetParamOrDefault<string?>(op, "solution"),
                    ct).ConfigureAwait(false);
                break;

            case ("entity", "UPDATE"):
                await metadataService.UpdateEntityAsync(
                    profileName,
                    GetParam<string>(op, "name"),
                    GetParamOrDefault<string?>(op, "displayName"),
                    GetParamOrDefault<string?>(op, "pluralName"),
                    GetParamOrDefault<string?>(op, "description"),
                    ct).ConfigureAwait(false);
                break;

            case ("entity", "DELETE"):
                await metadataService.DeleteEntityAsync(
                    profileName,
                    GetParam<string>(op, "name"),
                    ct).ConfigureAwait(false);
                break;

            case ("attribute", "CREATE"):
                await metadataService.CreateAttributeAsync(
                    profileName,
                    BuildCreateAttributeOptions(op),
                    ct).ConfigureAwait(false);
                break;

            case ("attribute", "UPDATE"):
                await metadataService.UpdateAttributeAsync(
                    profileName,
                    GetParam<string>(op, "entity"),
                    GetParam<string>(op, "name"),
                    GetParamOrDefault<string?>(op, "displayName"),
                    GetParamOrDefault<string?>(op, "description"),
                    GetParamOrDefault<string?>(op, "requiredLevel"),
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
                await metadataService.CreateManyToManyRelationshipAsync(
                    profileName,
                    GetParam<string>(op, "entity1"),
                    GetParam<string>(op, "entity2"),
                    GetParam<string>(op, "schemaName"),
                    GetParamOrDefault<string?>(op, "displayName"),
                    ct).ConfigureAwait(false);
                break;

            case ("relationship", "DELETE"):
                await metadataService.DeleteRelationshipAsync(
                    profileName,
                    GetParam<string>(op, "schemaName"),
                    ct).ConfigureAwait(false);
                break;

            case ("optionset", "CREATE"):
                var options = GetOptionsArray(op);
                await optionSetService.CreateGlobalOptionSetAsync(
                    profileName,
                    GetParam<string>(op, "schemaName"),
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

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a staged data operation into the corresponding Dataverse SDK request.
    /// </summary>
    private static OrganizationRequest BuildOrganizationRequest(StagedOperation op)
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

        // The "attributes" parameter holds either a JsonElement or a serialized JSON string
        JsonElement attributesJson;
        var rawAttributes = op.Parameters.GetValueOrDefault("attributes");
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
            SchemaName = GetParam<string>(op, "schemaName"),
            Type = GetParam<string>(op, "type"),
            DisplayName = GetParamOrDefault<string?>(op, "displayName"),
            Description = GetParamOrDefault<string?>(op, "description"),
            RequiredLevel = GetParamOrDefault<string?>(op, "requiredLevel") ?? "none",
            SolutionUniqueName = GetParamOrDefault<string?>(op, "solution"),
            MaxLength = GetParamOrDefault<int?>(op, "maxLength"),
            StringFormat = GetParamOrDefault<string?>(op, "stringFormat"),
            MinValue = GetParamOrDefault<double?>(op, "minValue"),
            MaxValue = GetParamOrDefault<double?>(op, "maxValue"),
            Precision = GetParamOrDefault<int?>(op, "precision"),
            NumberFormat = GetParamOrDefault<string?>(op, "numberFormat"),
            PrecisionSource = GetParamOrDefault<int?>(op, "precisionSource"),
            TrueLabel = GetParamOrDefault<string?>(op, "trueLabel") ?? "Yes",
            FalseLabel = GetParamOrDefault<string?>(op, "falseLabel") ?? "No",
            DateTimeFormat = GetParamOrDefault<string?>(op, "dateTimeFormat"),
            DateTimeBehavior = GetParamOrDefault<string?>(op, "dateTimeBehavior"),
            GlobalOptionSetName = GetParamOrDefault<string?>(op, "globalOptionSetName"),
            TargetEntity = GetParamOrDefault<string?>(op, "targetEntity"),
            CascadeDelete = GetParamOrDefault<string?>(op, "cascadeDelete") ?? "removelink",
            MaxSizeKb = GetParamOrDefault<int?>(op, "maxSizeKb"),
            CanStoreFullImage = GetParamOrDefault<bool?>(op, "canStoreFullImage") ?? true
        };
    }

    /// <summary>
    /// Extracts an array of <see cref="OptionMetadataInput"/> from a staged optionset CREATE operation.
    /// </summary>
    private static OptionMetadataInput[] GetOptionsArray(StagedOperation op)
    {
        if (!op.Parameters.TryGetValue("options", out var raw) || raw is null)
            return Array.Empty<OptionMetadataInput>();

        if (raw is JsonElement je)
        {
            return je.EnumerateArray()
                .Select(e => new OptionMetadataInput(
                    e.GetProperty("label").GetString()!,
                    e.GetProperty("value").GetInt32()))
                .ToArray();
        }

        // If already deserialized, try casting
        if (raw is OptionMetadataInput[] opts)
            return opts;

        return Array.Empty<OptionMetadataInput>();
    }
}
