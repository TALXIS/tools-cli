using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Shared output-formatting logic for all three bulk CLI commands.
/// </summary>
internal static class BulkOutputHelper
{
    public static void WriteResult(string operationName, BulkOperationResult result)
    {
        var payload = new
        {
            operation = operationName,
            succeeded = result.SucceededCount,
            failed = result.FailedCount,
            createdIds = result.CreatedIds.Select(id => id.ToString()).ToList(),
            failures = (result.Failures ?? Array.Empty<BulkOperationFailure>())
                .Select(f => new
                {
                    recordId = f.RecordId?.ToString(),
                    error = f.ErrorMessage
                })
                .ToList()
        };

        OutputFormatter.WriteData(payload, _ =>
        {
            OutputWriter.WriteLine($"{operationName}: {result.SucceededCount} succeeded, {result.FailedCount} failed.");
            if (result.CreatedIds.Count > 0)
            {
                OutputWriter.WriteLine("Created IDs:");
                foreach (var id in result.CreatedIds)
                {
                    OutputWriter.WriteLine($"  {id}");
                }
            }

            if (result.Failures is { Count: > 0 })
            {
                OutputWriter.WriteLine("Failures:");
                foreach (var failure in result.Failures)
                {
                    OutputWriter.WriteLine(
                        failure.RecordId.HasValue
                            ? $"  {failure.RecordId}: {failure.ErrorMessage}"
                            : $"  {failure.ErrorMessage}");
                }
            }
        });
    }
}
