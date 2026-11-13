namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Result of a bulk operation that runs in batches and stops at the first failed batch.
/// </summary>
/// <param name="SucceededCount">Records in batches that Dataverse committed.</param>
/// <param name="FailedCount">Records in the batch that Dataverse rejected.</param>
/// <param name="CreatedIds">IDs of the created records.</param>
/// <param name="UnknownCount">Records in a batch that timed out on the client; Dataverse may still have committed them.</param>
/// <param name="SkippedCount">Records after the failed batch that were not sent.</param>
/// <param name="Error">Why the run stopped, or <see langword="null"/> when every batch succeeded.</param>
public sealed record BulkOperationResult(
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<Guid> CreatedIds,
    int UnknownCount = 0,
    int SkippedCount = 0,
    string? Error = null);
