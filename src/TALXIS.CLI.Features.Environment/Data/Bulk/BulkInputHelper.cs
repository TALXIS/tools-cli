using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Shared input-parsing logic used by all three bulk CLI commands.
/// Validates that exactly one of <c>--file</c> / <c>--data</c> is supplied
/// and deserializes the JSON array into a list of <see cref="JsonElement"/>.
/// </summary>
internal static class BulkInputHelper
{
    public static bool TryParseRecords(
        string? filePath,
        string? inlineData,
        ILogger logger,
        [NotNullWhen(true)] out List<JsonElement>? records)
    {
        records = null;

        // Validate mutually-exclusive input sources.
        if (string.IsNullOrWhiteSpace(filePath) && string.IsNullOrWhiteSpace(inlineData))
        {
            logger.LogError("Provide either --file or --data with a JSON array of records.");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(filePath) && !string.IsNullOrWhiteSpace(inlineData))
        {
            logger.LogError("Provide only one of --file or --data, not both.");
            return false;
        }

        string json;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (!File.Exists(filePath))
            {
                logger.LogError("File not found: {Path}", filePath);
                return false;
            }
            json = File.ReadAllText(filePath);
        }
        else
        {
            json = inlineData!;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogError("Expected a JSON array of records.");
                return false;
            }

            records = new List<JsonElement>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                // Clone so the elements survive the JsonDocument disposal.
                records.Add(element.Clone());
            }

            if (records.Count == 0)
            {
                logger.LogError("The JSON array is empty — nothing to process.");
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError("Invalid JSON: {Error}", ex.Message);
            return false;
        }
    }
}
