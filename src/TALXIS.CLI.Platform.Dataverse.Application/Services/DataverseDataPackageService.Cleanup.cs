using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed partial class DataverseDataPackageService
{
    /// <summary>
    /// Dataverse <c>OrganizationServiceFault.ErrorCode</c> for "record does not exist".
    /// </summary>
    private const int ObjectDoesNotExistErrorCode = -2147220969;

    public async Task<DataPackageCleanupResult> CleanupAsync(
        string? profileName,
        string dataPackagePath,
        DataPackageCleanupOptions options,
        bool verbose,
        CancellationToken ct)
    {
        try
        {
            await DataverseCommandBridge.PrimeTokenAsync(profileName, ct).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            return EmptyCleanupResult(interactiveAuthRequired: true);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            return EmptyCleanupResult(errorMessage: ex.Message);
        }

        DataPackageContents contents;
        try
        {
            contents = DataPackageReader.Load(Path.GetFullPath(dataPackagePath));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException)
        {
            return EmptyCleanupResult(errorMessage: ex.Message);
        }

        var connectionCount = Math.Max(1, options.ConnectionCount);
        var connections = new List<DataverseConnection>(connectionCount);
        try
        {
            for (int i = 0; i < connectionCount; i++)
            {
                connections.Add(await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false));
            }

            var primary = connections[0].Client;

            int m2mCount = await ProcessM2mAsync(primary, contents.M2mAssociations, options, ct).ConfigureAwait(false);

            var order = BuildCleanupOrder(contents);
            var entityResults = await ProcessEntitiesAsync(connections, contents, order, options, ct).ConfigureAwait(false);

            var succeeded = entityResults.All(r => r.Errors == 0);
            return new DataPackageCleanupResult(
                Succeeded: succeeded,
                ErrorMessage: null,
                InteractiveAuthRequired: false,
                EntityResults: entityResults,
                TotalDeletedByGuid: entityResults.Sum(r => r.DeletedByGuid),
                TotalDeletedByNaturalKey: entityResults.Sum(r => r.DeletedByNaturalKey),
                TotalNotFound: entityResults.Sum(r => r.NotFound),
                TotalErrors: entityResults.Sum(r => r.Errors),
                M2mDisassociations: m2mCount);
        }
        finally
        {
            foreach (var c in connections)
                c.Dispose();
        }
    }

    /// <summary>
    /// Returns the entity order to process during cleanup: the schema's
    /// <c>&lt;entityImportOrder&gt;</c> reversed, plus any entity with records
    /// that wasn't listed in import order (appended in the order they appear
    /// in <c>data.xml</c>).
    /// </summary>
    internal static IReadOnlyList<string> BuildCleanupOrder(DataPackageContents contents)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        for (int i = contents.EntityImportOrder.Count - 1; i >= 0; i--)
        {
            var name = contents.EntityImportOrder[i];
            if (seen.Add(name))
                order.Add(name);
        }

        foreach (var name in contents.Records.Keys)
        {
            if (seen.Add(name))
                order.Add(name);
        }

        return order;
    }

    private async Task<int> ProcessM2mAsync(
        ServiceClient client,
        IReadOnlyList<DataPackageM2mAssociation> associations,
        DataPackageCleanupOptions options,
        CancellationToken ct)
    {
        if (associations.Count == 0)
            return 0;

        if (options.DryRun)
            return associations.Count;

        int processed = 0;
        var batchSize = Math.Max(1, options.BatchSize);

        // Group by (relationship, source) so we can disassociate many targets in one request.
        var groups = associations
            .GroupBy(a => (a.RelationshipName, a.SourceEntity, a.SourceId, a.TargetEntity))
            .ToList();

        var requests = new OrganizationRequestCollection();
        foreach (var group in groups)
        {
            var sourceRef = new EntityReference(group.Key.SourceEntity, group.Key.SourceId);
            var relatedRefs = new EntityReferenceCollection();
            foreach (var pair in group)
                relatedRefs.Add(new EntityReference(group.Key.TargetEntity, pair.TargetId));

            requests.Add(new DisassociateRequest
            {
                Target = sourceRef,
                RelatedEntities = relatedRefs,
                Relationship = new Relationship(group.Key.RelationshipName),
            });

            processed += relatedRefs.Count;

            if (requests.Count >= batchSize)
            {
                await ExecuteMultipleIgnoringMissingAsync(client, requests, options.ContinueOnError, ct).ConfigureAwait(false);
                requests = new OrganizationRequestCollection();
            }
        }

        if (requests.Count > 0)
            await ExecuteMultipleIgnoringMissingAsync(client, requests, options.ContinueOnError, ct).ConfigureAwait(false);

        return processed;
    }

    private static async Task ExecuteMultipleIgnoringMissingAsync(
        ServiceClient client,
        OrganizationRequestCollection requests,
        bool continueOnError,
        CancellationToken ct)
    {
        await client.ExecuteAsync(new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings
            {
                ContinueOnError = continueOnError,
                ReturnResponses = true,
            },
            Requests = requests,
        }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DataPackageCleanupEntityResult>> ProcessEntitiesAsync(
        IReadOnlyList<DataverseConnection> connections,
        DataPackageContents contents,
        IReadOnlyList<string> order,
        DataPackageCleanupOptions options,
        CancellationToken ct)
    {
        var results = new List<DataPackageCleanupEntityResult>();
        var resultsLock = new object();

        if (connections.Count > 1)
        {
            var indexCounter = -1;
            await Parallel.ForEachAsync(
                order,
                new ParallelOptions { MaxDegreeOfParallelism = connections.Count, CancellationToken = ct },
                async (entityName, innerCt) =>
                {
                    var idx = Interlocked.Increment(ref indexCounter);
                    var client = connections[idx % connections.Count].Client;
                    var r = await CleanupEntityAsync(client, entityName, contents, options, innerCt).ConfigureAwait(false);
                    lock (resultsLock)
                        results.Add(r);
                }).ConfigureAwait(false);
        }
        else
        {
            var primary = connections[0].Client;
            foreach (var entityName in order)
            {
                ct.ThrowIfCancellationRequested();
                var r = await CleanupEntityAsync(primary, entityName, contents, options, ct).ConfigureAwait(false);
                results.Add(r);
                if (!options.ContinueOnError && r.Errors > 0)
                    break;
            }
        }

        return results;
    }

    private static async Task<DataPackageCleanupEntityResult> CleanupEntityAsync(
        ServiceClient client,
        string entityName,
        DataPackageContents contents,
        DataPackageCleanupOptions options,
        CancellationToken ct)
    {
        if (!contents.Records.TryGetValue(entityName, out var records) || records.Count == 0)
        {
            return new DataPackageCleanupEntityResult(entityName, 0, 0, 0, 0, 0, Array.Empty<string>());
        }

        contents.Schemas.TryGetValue(entityName, out var schema);

        int deletedByGuid = 0;
        int deletedByNaturalKey = 0;
        int notFound = 0;
        int errors = 0;
        var errorMessages = new List<string>();

        var batchSize = Math.Max(1, options.BatchSize);

        for (int offset = 0; offset < records.Count; offset += batchSize)
        {
            var chunk = records.Skip(offset).Take(batchSize).ToList();

            if (options.DryRun)
            {
                deletedByGuid += chunk.Count;
                continue;
            }

            var requests = new OrganizationRequestCollection();
            foreach (var r in chunk)
                requests.Add(new DeleteRequest { Target = new EntityReference(entityName, r.Id) });

            ExecuteMultipleResponse response;
            try
            {
                response = (ExecuteMultipleResponse)await client.ExecuteAsync(new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true,
                    },
                    Requests = requests,
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors += chunk.Count;
                errorMessages.Add(ex.Message);
                if (!options.ContinueOnError)
                    return new DataPackageCleanupEntityResult(
                        entityName, records.Count, deletedByGuid, deletedByNaturalKey, notFound, errors, errorMessages);
                continue;
            }

            for (int i = 0; i < chunk.Count; i++)
            {
                var record = chunk[i];
                var item = response.Responses.FirstOrDefault(r => r.RequestIndex == i);
                if (item is null || item.Fault is null)
                {
                    deletedByGuid++;
                    continue;
                }

                var fault = item.Fault;
                if (fault.ErrorCode == ObjectDoesNotExistErrorCode)
                {
                    var fallback = await TryNaturalKeyDeleteAsync(client, entityName, record, schema, options, ct).ConfigureAwait(false);
                    switch (fallback.Outcome)
                    {
                        case NaturalKeyOutcome.DeletedByNaturalKey:
                            deletedByNaturalKey++;
                            break;
                        case NaturalKeyOutcome.NotFound:
                            notFound++;
                            if (options.MissingAction == DataPackageCleanupMissingAction.Fail)
                            {
                                errors++;
                                errorMessages.Add($"{entityName} {record.Id}: not found by id or natural key.");
                                return new DataPackageCleanupEntityResult(
                                    entityName, records.Count, deletedByGuid, deletedByNaturalKey, notFound, errors, errorMessages);
                            }
                            break;
                        case NaturalKeyOutcome.Error:
                            errors++;
                            if (fallback.ErrorMessage is not null)
                                errorMessages.Add($"{entityName} {record.Id}: {fallback.ErrorMessage}");
                            if (!options.ContinueOnError)
                                return new DataPackageCleanupEntityResult(
                                    entityName, records.Count, deletedByGuid, deletedByNaturalKey, notFound, errors, errorMessages);
                            break;
                    }
                }
                else
                {
                    errors++;
                    errorMessages.Add($"{entityName} {record.Id}: {fault.Message}");
                    if (!options.ContinueOnError)
                        return new DataPackageCleanupEntityResult(
                            entityName, records.Count, deletedByGuid, deletedByNaturalKey, notFound, errors, errorMessages);
                }
            }
        }

        return new DataPackageCleanupEntityResult(
            entityName, records.Count, deletedByGuid, deletedByNaturalKey, notFound, errors, errorMessages);
    }

    private static async Task<NaturalKeyResult> TryNaturalKeyDeleteAsync(
        ServiceClient client,
        string entityName,
        DataPackageRecordRow record,
        DataPackageEntitySchema? schema,
        DataPackageCleanupOptions options,
        CancellationToken ct)
    {
        if (options.MissingAction != DataPackageCleanupMissingAction.ByNaturalKey)
            return new NaturalKeyResult(NaturalKeyOutcome.NotFound, null);

        if (schema is null)
            return new NaturalKeyResult(NaturalKeyOutcome.NotFound, null);

        var primaryId = schema.PrimaryIdField;
        if (string.IsNullOrWhiteSpace(primaryId))
            return new NaturalKeyResult(NaturalKeyOutcome.NotFound, null);

        // Build conditions from every natural-key column that has a value on the record.
        var conditions = new List<ConditionExpression>();
        foreach (var fieldName in schema.NaturalKeyFields)
        {
            if (!record.Fields.TryGetValue(fieldName, out var value) || string.IsNullOrEmpty(value))
                continue;
            conditions.Add(new ConditionExpression(fieldName, ConditionOperator.Equal, value));
        }

        if (conditions.Count == 0)
            return new NaturalKeyResult(NaturalKeyOutcome.NotFound, null);

        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(primaryId),
            TopCount = 2,
        };
        foreach (var c in conditions)
            query.Criteria.Conditions.Add(c);

        EntityCollection matches;
        try
        {
            matches = await client.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new NaturalKeyResult(NaturalKeyOutcome.Error, ex.Message);
        }

        if (matches.Entities.Count == 0)
            return new NaturalKeyResult(NaturalKeyOutcome.NotFound, null);

        if (matches.Entities.Count > 1)
            return new NaturalKeyResult(NaturalKeyOutcome.Error, "ambiguous natural-key match.");

        try
        {
            await client.DeleteAsync(entityName, matches.Entities[0].Id, ct).ConfigureAwait(false);
            return new NaturalKeyResult(NaturalKeyOutcome.DeletedByNaturalKey, null);
        }
        catch (Exception ex)
        {
            return new NaturalKeyResult(NaturalKeyOutcome.Error, ex.Message);
        }
    }

    private static DataPackageCleanupResult EmptyCleanupResult(
        bool interactiveAuthRequired = false, string? errorMessage = null)
        => new(
            Succeeded: false,
            ErrorMessage: errorMessage,
            InteractiveAuthRequired: interactiveAuthRequired,
            EntityResults: Array.Empty<DataPackageCleanupEntityResult>(),
            TotalDeletedByGuid: 0,
            TotalDeletedByNaturalKey: 0,
            TotalNotFound: 0,
            TotalErrors: 0,
            M2mDisassociations: 0);

    private enum NaturalKeyOutcome
    {
        DeletedByNaturalKey,
        NotFound,
        Error,
    }

    private sealed record NaturalKeyResult(NaturalKeyOutcome Outcome, string? ErrorMessage);
}
