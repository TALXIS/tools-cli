using System.Text.Json;

namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Result of a bulk operation — per-record outcomes.
/// </summary>
public sealed record BulkOperationFailure(
    Guid? RecordId,
    string ErrorMessage);

/// <summary>
/// Result of a bulk operation — aggregate counts plus optional failure details.
/// </summary>
public sealed record BulkOperationResult(
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<Guid> CreatedIds,
    IReadOnlyList<BulkOperationFailure>? Failures = null);

/// <summary>
/// Bulk operations against a live Dataverse environment using
/// <c>CreateMultiple</c>, <c>UpdateMultiple</c>, <c>UpsertMultiple</c>, and
/// <c>DeleteMultiple</c> SDK messages via <c>ServiceClient.Execute()</c>.
/// </summary>
public interface IDataverseBulkService
{
    /// <summary>
    /// Creates multiple records of the same entity type in a single request
    /// using the <c>CreateMultipleRequest</c> SDK message.
    /// </summary>
    Task<BulkOperationResult> CreateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct);

    /// <summary>
    /// Updates multiple records of the same entity type in a single request
    /// using the <c>UpdateMultipleRequest</c> SDK message.
    /// </summary>
    Task<BulkOperationResult> UpdateMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct);

    /// <summary>
    /// Upserts (creates or updates) multiple records of the same entity type
    /// in a single request using the <c>UpsertMultipleRequest</c> SDK message.
    /// </summary>
    Task<BulkOperationResult> UpsertMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<JsonElement> records,
        CancellationToken ct);

    /// <summary>
    /// Deletes multiple records of the same entity type using the
    /// <c>DeleteMultiple</c> SDK message when available, falling back to
    /// <c>ExecuteMultiple</c> with individual <c>DeleteRequest</c> items.
    /// </summary>
    Task<BulkOperationResult> DeleteMultipleAsync(
        string? profileName,
        string entityLogicalName,
        IReadOnlyList<Guid> recordIds,
        int batchSize,
        CancellationToken ct);
}
