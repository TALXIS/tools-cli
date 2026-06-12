using System.Security;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Dataverse implementation of <see cref="IDataverseBulkService"/>.
/// Delegates to the SDK <c>CreateMultiple</c>, <c>UpdateMultiple</c>, and
/// <c>UpsertMultiple</c> messages via <see cref="DataverseCommandBridge"/>.
/// </summary>
internal sealed class DataverseBulkService : IDataverseBulkService
{
    /// <inheritdoc />
    public async Task<BulkOperationResult> CreateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var metadata = await RetrieveAttributeMetadataAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
        var entities = new EntityCollection(
            records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r, metadata)).ToList())
        {
            EntityName = entityLogicalName
        };

        var request = new CreateMultipleRequest { Targets = entities };
        var response = (CreateMultipleResponse)await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        return new BulkOperationResult(
            SucceededCount: response.Ids.Length,
            FailedCount: records.Count - response.Ids.Length,
            CreatedIds: response.Ids.ToList(),
            Failures: Array.Empty<BulkOperationFailure>());
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> UpdateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var metadata = await RetrieveAttributeMetadataAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
        // Each record must contain the entity primary ID field so the SDK
        // knows which record to update.  The ID is resolved inside
        // EntityJsonConverter.JsonToEntity via the standard attribute mapping;
        // the caller is responsible for including the primary key field
        // (e.g. "accountid") in the JSON payload.
        var entities = new EntityCollection(
            records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r, metadata)).ToList())
        {
            EntityName = entityLogicalName
        };

        var request = new UpdateMultipleRequest { Targets = entities };
        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        // UpdateMultipleResponse does not return per-record IDs.
        return new BulkOperationResult(
            SucceededCount: records.Count,
            FailedCount: 0,
            CreatedIds: Array.Empty<Guid>(),
            Failures: Array.Empty<BulkOperationFailure>());
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> UpsertMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var metadata = await RetrieveAttributeMetadataAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
        var entities = new EntityCollection(
            records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r, metadata)).ToList())
        {
            EntityName = entityLogicalName
        };

        var request = new UpsertMultipleRequest { Targets = entities };
        var response = (UpsertMultipleResponse)await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        // Each UpsertResult indicates whether the record was Created or Updated
        // and exposes the record ID for created records.
        var createdIds = new List<Guid>();
        int created = 0;
        int updated = 0;

        foreach (UpsertResponse result in response.Results.Cast<UpsertResponse>())
        {
            if (result.RecordCreated)
            {
                created++;
                createdIds.Add(result.Target.Id);
            }
            else
            {
                updated++;
            }
        }

        return new BulkOperationResult(
            SucceededCount: created + updated,
            FailedCount: records.Count - (created + updated),
            CreatedIds: createdIds,
            Failures: Array.Empty<BulkOperationFailure>());
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> DeleteMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<Guid> recordIds,
        int batchSize,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityLogicalName);

        if (batchSize is <= 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 200.");

        if (recordIds.Count == 0)
        {
            return new BulkOperationResult(
                SucceededCount: 0,
                FailedCount: 0,
                CreatedIds: Array.Empty<Guid>(),
                Failures: Array.Empty<BulkOperationFailure>());
        }

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var supportsDeleteMultiple = false;
        try
        {
            supportsDeleteMultiple = await IsMessageAvailableAsync(conn, entityLogicalName, "DeleteMultiple", ct).ConfigureAwait(false);
        }
        catch
        {
            // If the capability probe itself fails, fall back to ExecuteMultiple.
            supportsDeleteMultiple = false;
        }
        var succeededCount = 0;
        var failures = new List<BulkOperationFailure>();

        foreach (var batch in recordIds.Chunk(batchSize))
        {
            ct.ThrowIfCancellationRequested();

            if (supportsDeleteMultiple)
            {
                try
                {
                    var request = new DeleteMultipleRequest
                    {
                        Targets = new EntityReferenceCollection(
                            batch.Select(id => new EntityReference(entityLogicalName, id)).ToList())
                    };

                    await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
                    succeededCount += batch.Length;
                    continue;
                }
                catch
                {
                    // Fall back to ExecuteMultiple to surface per-record failures.
                }
            }

            var fallbackResult = await DeleteViaExecuteMultipleAsync(conn, entityLogicalName, batch, ct).ConfigureAwait(false);
            succeededCount += fallbackResult.SucceededCount;
            if (fallbackResult.Failures is { Count: > 0 })
                failures.AddRange(fallbackResult.Failures);
        }

        return new BulkOperationResult(
            SucceededCount: succeededCount,
            FailedCount: failures.Count,
            CreatedIds: Array.Empty<Guid>(),
            Failures: failures);
    }

    private static async Task<EntityMetadata> RetrieveAttributeMetadataAsync(
        DataverseConnection conn, string entityLogicalName, CancellationToken ct)
    {
        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true
        };
        var response = (RetrieveEntityResponse)await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);
        return response.EntityMetadata;
    }

    private static async Task<BulkOperationResult> DeleteViaExecuteMultipleAsync(
        DataverseConnection conn,
        string entityLogicalName,
        IReadOnlyList<Guid> recordIds,
        CancellationToken ct)
    {
        var requests = new OrganizationRequestCollection();
        foreach (var recordId in recordIds)
        {
            requests.Add(new DeleteRequest
            {
                Target = new EntityReference(entityLogicalName, recordId)
            });
        }

        var response = (ExecuteMultipleResponse)await conn.Client.ExecuteAsync(
            new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = true
                },
                Requests = requests
            }, ct).ConfigureAwait(false);

        var failures = new List<BulkOperationFailure>();
        foreach (var item in response.Responses)
        {
            if (item.Fault is not null)
                failures.Add(new BulkOperationFailure(recordIds[item.RequestIndex], item.Fault.Message));
        }

        return new BulkOperationResult(
            SucceededCount: recordIds.Count - failures.Count,
            FailedCount: failures.Count,
            CreatedIds: Array.Empty<Guid>(),
            Failures: failures);
    }

    private static async Task<bool> IsMessageAvailableAsync(
        DataverseConnection conn,
        string entityLogicalName,
        string messageName,
        CancellationToken ct)
    {
        // primaryobjecttypecode is an integer (entity object type code), not a string.
        // Filtering it by logical name throws FormatException. DeleteMultiple/CreateMultiple/UpdateMultiple
        // support is org-level — checking by message name alone is sufficient.
        var fetchXml = $"""
            <fetch top='1'>
              <entity name='sdkmessage'>
                <attribute name='sdkmessageid' />
                <filter>
                  <condition attribute='name' operator='eq' value='{XmlEscape(messageName)}' />
                </filter>
              </entity>
            </fetch>
            """;

        var response = await conn.Client.RetrieveMultipleAsync(new FetchExpression(fetchXml), ct).ConfigureAwait(false);
        return response.Entities.Count > 0;
    }

    private static string XmlEscape(string value) => SecurityElement.Escape(value) ?? value;
}
