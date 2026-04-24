using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
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

        var entities = new EntityCollection(
            records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r)).ToList())
        {
            EntityName = entityLogicalName
        };

        var request = new CreateMultipleRequest { Targets = entities };
        var response = (CreateMultipleResponse)await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        return new BulkOperationResult(
            SucceededCount: response.Ids.Length,
            FailedCount: records.Count - response.Ids.Length,
            CreatedIds: response.Ids.ToList());
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> UpdateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Each record must contain the entity primary ID field so the SDK
        // knows which record to update.  The ID is resolved inside
        // EntityJsonConverter.JsonToEntity via the standard attribute mapping;
        // the caller is responsible for including the primary key field
        // (e.g. "accountid") in the JSON payload.
        var entities = new EntityCollection(
            records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r)).ToList())
        {
            EntityName = entityLogicalName
        };

        var request = new UpdateMultipleRequest { Targets = entities };
        await conn.Client.ExecuteAsync(request, ct).ConfigureAwait(false);

        // UpdateMultipleResponse does not return per-record IDs.
        return new BulkOperationResult(
            SucceededCount: records.Count,
            FailedCount: 0,
            CreatedIds: Array.Empty<Guid>());
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> UpsertMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var entities = new EntityCollection(
            records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r)).ToList())
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
            CreatedIds: createdIds);
    }
}
