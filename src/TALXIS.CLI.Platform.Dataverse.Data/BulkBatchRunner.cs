using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Sends bulk records in fixed-size batches so a single request stays inside the client timeout.
/// </summary>
internal static class BulkBatchRunner
{
    public static async Task<BulkOperationResult> RunAsync(
        string entityLogicalName,
        IReadOnlyList<Entity> entities,
        int batchSize,
        Func<EntityCollection, CancellationToken, Task<(int Succeeded, IReadOnlyList<Guid> CreatedIds)>> executeBatch,
        CancellationToken ct)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1.");

        var succeeded = 0;
        var createdIds = new List<Guid>();

        for (var start = 0; start < entities.Count; start += batchSize)
        {
            var batch = entities.Skip(start).Take(batchSize).ToList();
            var collection = new EntityCollection(batch) { EntityName = entityLogicalName };

            try
            {
                var (batchSucceeded, batchCreatedIds) = await executeBatch(collection, ct).ConfigureAwait(false);
                succeeded += batchSucceeded;
                createdIds.AddRange(batchCreatedIds);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var range = $"records {start + 1}-{start + batch.Count} of {entities.Count}";
                var skipped = entities.Count - start - batch.Count;

                if (IsTimeout(ex))
                {
                    var earlier = start > 0 ? " Earlier batches were committed." : "";
                    return new BulkOperationResult(
                        succeeded, 0, createdIds, UnknownCount: batch.Count, SkippedCount: skipped,
                        Error: $"The batch with {range} timed out on the client. Dataverse may still have committed it, so check those records before you retry.{earlier} Use a smaller --batch-size for tables with slow plugins.");
                }

                return new BulkOperationResult(
                    succeeded, batch.Count, createdIds, SkippedCount: skipped,
                    Error: $"The batch with {range} failed and was not committed: {ex.Message}");
            }
        }

        return new BulkOperationResult(succeeded, 0, createdIds);
    }

    internal static bool IsTimeout(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or TaskCanceledException) return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(IsTimeout)) return true;
        }

        return false;
    }
}
