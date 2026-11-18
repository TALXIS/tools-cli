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
            unknown = result.UnknownCount,
            skipped = result.SkippedCount,
            createdIds = result.CreatedIds.Select(id => id.ToString()).ToList(),
            error = result.Error
        };

        OutputFormatter.WriteData(payload, _ =>
        {
            OutputWriter.WriteLine($"{operationName}: {result.SucceededCount} succeeded, {result.FailedCount} failed, {result.UnknownCount} unknown, {result.SkippedCount} not sent.");
            if (result.CreatedIds.Count > 0)
            {
                OutputWriter.WriteLine("Created IDs:");
                foreach (var id in result.CreatedIds)
                {
                    OutputWriter.WriteLine($"  {id}");
                }
            }
        });
    }
}
