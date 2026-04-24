using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Shared output-formatting logic for all three bulk CLI commands.
/// </summary>
internal static class BulkOutputHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void WriteResult(string operationName, BulkOperationResult result, bool asJson)
    {
        if (asJson)
        {
            var payload = new
            {
                operation = operationName,
                succeeded = result.SucceededCount,
                failed = result.FailedCount,
                createdIds = result.CreatedIds.Select(id => id.ToString()).ToList()
            };
            OutputWriter.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        OutputWriter.WriteLine($"{operationName}: {result.SucceededCount} succeeded, {result.FailedCount} failed.");
        if (result.CreatedIds.Count > 0)
        {
            OutputWriter.WriteLine("Created IDs:");
            foreach (var id in result.CreatedIds)
            {
                OutputWriter.WriteLine($"  {id}");
            }
        }
    }
}
