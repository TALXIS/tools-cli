using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Implements single-record CRUD operations against a live Dataverse
/// environment using the <c>ServiceClient</c> SDK.
/// </summary>
internal sealed class DataverseRecordService : IDataverseRecordService
{
    public async Task<JsonElement> GetAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        string[]? columns,
        bool includeAnnotations,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var columnSet = columns is { Length: > 0 }
            ? new ColumnSet(columns)
            : new ColumnSet(true);

        var entity = await conn.Client.RetrieveAsync(
            entityLogicalName, recordId, columnSet, ct).ConfigureAwait(false);

        return EntityJsonConverter.EntityToJson(entity, includeAnnotations);
    }

    public async Task<Guid> CreateAsync(
        string? profileName,
        string entityLogicalName,
        JsonElement attributes,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var entity = EntityJsonConverter.JsonToEntity(entityLogicalName, attributes);

        return await conn.Client.CreateAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        JsonElement attributes,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var entity = EntityJsonConverter.JsonToEntity(entityLogicalName, attributes, recordId);

        await conn.Client.UpdateAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string? profileName,
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        await conn.Client.DeleteAsync(entityLogicalName, recordId, ct).ConfigureAwait(false);
    }
}
