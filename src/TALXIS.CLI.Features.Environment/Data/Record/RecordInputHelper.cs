using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Shared input parsing for record create/update commands.
/// Validates <c>--data</c> / <c>--file</c> mutual exclusion and parses
/// the JSON into a <see cref="JsonElement"/>.
/// </summary>
internal static class RecordInputHelper
{
    /// <summary>
    /// Validates that exactly one of <paramref name="data"/> / <paramref name="file"/>
    /// is provided and parses the JSON into a <see cref="JsonElement"/> object.
    /// </summary>
    public static bool TryParseAttributes(
        string? data,
        string? file,
        ILogger logger,
        out JsonElement attributes)
    {
        attributes = default;

        bool hasData = !string.IsNullOrWhiteSpace(data);
        bool hasFile = !string.IsNullOrWhiteSpace(file);

        if (hasData == hasFile)
        {
            logger.LogError("Provide exactly one of --data or --file.");
            return false;
        }

        string json;
        if (hasFile)
        {
            if (!File.Exists(file))
            {
                logger.LogError("File not found: {Path}", file);
                return false;
            }
            json = File.ReadAllText(file!);
        }
        else
        {
            json = data!;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogError("Expected a JSON object, got {Kind}.", document.RootElement.ValueKind);
                return false;
            }

            // Clone so the element remains valid after the JsonDocument is disposed.
            attributes = document.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError("Invalid JSON: {Error}", ex.Message);
            return false;
        }
    }
}
