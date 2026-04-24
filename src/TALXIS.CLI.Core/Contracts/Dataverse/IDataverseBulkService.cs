using System.Text.Json;

namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Result of a bulk operation — per-record outcomes.
/// </summary>
public sealed record BulkOperationResult(
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<Guid> CreatedIds);

/// <summary>
/// Bulk operations against a live Dataverse environment using
/// <c>CreateMultiple</c>, <c>UpdateMultiple</c>, and <c>UpsertMultiple</c>
/// SDK messages via <c>ServiceClient.Execute()</c>.
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
}
