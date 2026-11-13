using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Dataverse implementation of <see cref="IDataverseBulkService"/>.
/// Sends the SDK <c>CreateMultiple</c>, <c>UpdateMultiple</c>, and
/// <c>UpsertMultiple</c> messages in batches via <see cref="DataverseCommandBridge"/>.
/// </summary>
internal sealed class DataverseBulkService : IDataverseBulkService
{
    /// <inheritdoc />
    public async Task<BulkOperationResult> CreateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        int batchSize,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var entities = await ToEntitiesAsync(conn, entityLogicalName, records, ct).ConfigureAwait(false);

        return await BulkBatchRunner.RunAsync(entityLogicalName, entities, batchSize, async (batch, token) =>
        {
            var response = (CreateMultipleResponse)await conn.Client
                .ExecuteAsync(new CreateMultipleRequest { Targets = batch }, token).ConfigureAwait(false);
            return (response.Ids.Length, response.Ids);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> UpdateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        int batchSize,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        // The caller includes the primary key field (e.g. "accountid") so the SDK knows which record to update.
        var entities = await ToEntitiesAsync(conn, entityLogicalName, records, ct).ConfigureAwait(false);

        return await BulkBatchRunner.RunAsync(entityLogicalName, entities, batchSize, async (batch, token) =>
        {
            await conn.Client.ExecuteAsync(new UpdateMultipleRequest { Targets = batch }, token).ConfigureAwait(false);
            return (batch.Entities.Count, Array.Empty<Guid>());
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> UpsertMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        int batchSize,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var entities = await ToEntitiesAsync(conn, entityLogicalName, records, ct).ConfigureAwait(false);

        return await BulkBatchRunner.RunAsync(entityLogicalName, entities, batchSize, async (batch, token) =>
        {
            var response = (UpsertMultipleResponse)await conn.Client
                .ExecuteAsync(new UpsertMultipleRequest { Targets = batch }, token).ConfigureAwait(false);
            var results = response.Results.Cast<UpsertResponse>().ToList();
            IReadOnlyList<Guid> createdIds = results.Where(r => r.RecordCreated).Select(r => r.Target.Id).ToList();
            return (results.Count, createdIds);
        }, ct).ConfigureAwait(false);
    }

    private static async Task<List<Entity>> ToEntitiesAsync(
        DataverseConnection conn, string entityLogicalName, IReadOnlyList<JsonElement> records, CancellationToken ct)
    {
        var metadata = await RetrieveAttributeMetadataAsync(conn, entityLogicalName, ct).ConfigureAwait(false);
        return records.Select(r => EntityJsonConverter.JsonToEntity(entityLogicalName, r, metadata)).ToList();
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
}
