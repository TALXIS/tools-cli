using System.Text.Json;

namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Bulk operations against a live Dataverse environment using
/// <c>CreateMultiple</c>, <c>UpdateMultiple</c>, and <c>UpsertMultiple</c>
/// SDK messages via <c>ServiceClient.Execute()</c>.
/// </summary>
public interface IDataverseBulkService
{
    /// <summary>
    /// Creates multiple records of the same entity type in requests of at most
    /// <paramref name="batchSize"/> records using the <c>CreateMultipleRequest</c> SDK message.
    /// </summary>
    Task<BulkOperationResult> CreateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        int batchSize,
        CancellationToken ct);

    /// <summary>
    /// Updates multiple records of the same entity type in requests of at most
    /// <paramref name="batchSize"/> records using the <c>UpdateMultipleRequest</c> SDK message.
    /// </summary>
    Task<BulkOperationResult> UpdateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        int batchSize,
        CancellationToken ct);

    /// <summary>
    /// Upserts (creates or updates) multiple records of the same entity type
    /// in requests of at most <paramref name="batchSize"/> records using the <c>UpsertMultipleRequest</c> SDK message.
    /// </summary>
    Task<BulkOperationResult> UpsertMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        int batchSize,
        CancellationToken ct);
}
