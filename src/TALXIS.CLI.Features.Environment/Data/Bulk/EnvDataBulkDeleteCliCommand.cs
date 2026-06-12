using System.Text.Json;
using System.Xml.Linq;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Deletes multiple Dataverse records using IDs supplied directly, from a JSON
/// file, or resolved from a FetchXML query.
/// </summary>
[CliDestructive("Permanently deletes the matching records from the remote environment.")]
[CliCommand(
    Name = "delete",
    Description = "Deletes multiple Dataverse records in bulk on the LIVE connected environment. Requires an active profile."
)]
public class EnvDataBulkDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataBulkDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliArgument(Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--ids", Description = "Comma-separated list of record GUIDs.", Required = false)]
    public string? Ids { get; set; }

    [CliOption(Name = "--file", Description = "Path to a JSON file containing GUIDs or { id } objects.", Required = false)]
    public string? File { get; set; }

    [CliOption(Name = "--fetchxml", Description = "FetchXML query used to resolve record IDs to delete.", Required = false)]
    public string? FetchXml { get; set; }

    [CliOption(Name = "--batch-size", Description = "Number of records per request. Maximum 200.", Required = false)]
    public int BatchSize { get; set; } = 200;

    [CliOption(Name = "--dry-run", Description = "Preview matching records without deleting them.", Required = false)]
    public bool DryRun { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (BatchSize is <= 0 or > 200)
        {
            Logger.LogError("--batch-size must be between 1 and 200.");
            return ExitValidationError;
        }

        var recordIds = await ResolveRecordIdsAsync(CancellationToken.None).ConfigureAwait(false);
        if (recordIds is null)
            return ExitValidationError;

        var distinctIds = recordIds.Distinct().ToList();
        if (distinctIds.Count != recordIds.Count)
        {
            Logger.LogWarning(
                "Removed {DuplicateCount} duplicate record ID(s) before processing.",
                recordIds.Count - distinctIds.Count);
        }

        if (DryRun)
        {
            WriteDryRun(distinctIds);
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseBulkService>();
        var result = await service.DeleteMultipleAsync(Profile, Entity, distinctIds, BatchSize, CancellationToken.None)
            .ConfigureAwait(false);

        WriteDeleteResult(result);
        return ExitSuccess;
    }

    private async Task<List<Guid>?> ResolveRecordIdsAsync(CancellationToken ct)
    {
        var providedInputs = new[] { Ids, File, FetchXml }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (providedInputs != 1)
        {
            Logger.LogError("Provide exactly one of --ids, --file, or --fetchxml.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(Ids))
            return ParseIds(Ids);

        if (!string.IsNullOrWhiteSpace(File))
            return ParseIdsFromFile(File);

        return await ResolveIdsFromFetchXmlAsync(FetchXml!, ct).ConfigureAwait(false);
    }

    private List<Guid>? ParseIds(string ids)
    {
        var result = new List<Guid>();

        foreach (var part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                Logger.LogError("Invalid GUID in --ids: {Value}", part);
                return null;
            }

            result.Add(id);
        }

        if (result.Count == 0)
        {
            Logger.LogError("No GUIDs were provided in --ids.");
            return null;
        }

        return result;
    }

    private List<Guid>? ParseIdsFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Logger.LogError("File not found: {Path}", filePath);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(filePath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                Logger.LogError("Expected a JSON array of GUIDs or objects with an 'id' property.");
                return null;
            }

            var ids = new List<Guid>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(element.GetString(), out var stringId))
                {
                    ids.Add(stringId);
                }
                else if (TryReadGuidProperty(element, "id", out var objectId))
                {
                    ids.Add(objectId);
                }
                else
                {
                    Logger.LogError(
                        "Invalid entry at index {Index}. Expected a GUID string or an object with an 'id' property.",
                        index);
                    return null;
                }

                index++;
            }

            return ids;
        }
        catch (JsonException ex)
        {
            Logger.LogError("Invalid JSON: {Error}", ex.Message);
            return null;
        }
    }

    private async Task<List<Guid>?> ResolveIdsFromFetchXmlAsync(string fetchXml, CancellationToken ct)
    {
        var metadataService = TxcServices.Get<IDataverseEntityMetadataService>();
        var queryService = TxcServices.Get<IDataverseQueryService>();

        var detail = await metadataService.GetEntityDetailAsync(Profile, Entity, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(detail.PrimaryIdAttribute))
            throw new InvalidOperationException($"Entity '{Entity}' does not expose a primary ID attribute.");

        var effectiveFetchXml = EnsurePrimaryIdAttribute(fetchXml, detail.PrimaryIdAttribute);
        var result = await queryService.QueryFetchXmlAsync(Profile, effectiveFetchXml, null, false, ct).ConfigureAwait(false);

        var ids = new List<Guid>();
        foreach (var record in result.Records)
        {
            if (!TryReadGuid(record, detail.PrimaryIdAttribute, out var id))
            {
                Logger.LogError(
                    "FetchXML results must expose the '{PrimaryIdAttribute}' value so records can be deleted.",
                    detail.PrimaryIdAttribute);
                return null;
            }

            ids.Add(id);
        }

        return ids;
    }

    private void WriteDryRun(IReadOnlyList<Guid> recordIds)
    {
        var payload = new
        {
            operation = "DeleteMultiple",
            entity = Entity,
            dryRun = true,
            count = recordIds.Count,
            batchSize = BatchSize,
            ids = recordIds.Select(id => id.ToString()).ToList()
        };

        OutputFormatter.WriteData(payload, _ =>
        {
            OutputWriter.WriteLine($"Dry run: would delete {recordIds.Count} '{Entity}' record(s) in batches of {BatchSize}.");

            if (recordIds.Count > 0)
            {
                OutputWriter.WriteLine("Record IDs:");
                foreach (var recordId in recordIds)
                    OutputWriter.WriteLine($"  {recordId}");
            }
        });
    }

    private static void WriteDeleteResult(BulkOperationResult result)
    {
        var payload = new
        {
            operation = "DeleteMultiple",
            deleted = result.SucceededCount,
            failed = result.FailedCount,
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
            OutputWriter.WriteLine($"DeleteMultiple: {result.SucceededCount} deleted, {result.FailedCount} failed.");
            if (result.Failures is not { Count: > 0 })
                return;

            OutputWriter.WriteLine("Failures:");
            foreach (var failure in result.Failures)
            {
                OutputWriter.WriteLine(
                    failure.RecordId.HasValue
                        ? $"  {failure.RecordId}: {failure.ErrorMessage}"
                        : $"  {failure.ErrorMessage}");
            }
        });
    }

    private static string EnsurePrimaryIdAttribute(string fetchXml, string primaryIdAttribute)
    {
        var document = XDocument.Parse(fetchXml, LoadOptions.PreserveWhitespace);
        var entityElement = document.Root?.Element("entity");
        if (entityElement is null)
            return fetchXml;

        var hasPrimaryId = entityElement.Elements("attribute")
            .Any(e => string.Equals((string?)e.Attribute("name"), primaryIdAttribute, StringComparison.OrdinalIgnoreCase));

        if (!hasPrimaryId)
            entityElement.AddFirst(new XElement("attribute", new XAttribute("name", primaryIdAttribute)));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool TryReadGuid(JsonElement record, string primaryIdAttribute, out Guid id)
    {
        if (TryReadGuidProperty(record, primaryIdAttribute, out id))
            return true;

        if (TryReadGuidProperty(record, "id", out id))
            return true;

        foreach (var property in record.EnumerateObject())
        {
            if (!property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(property.Value.GetString(), out id))
            {
                return true;
            }
        }

        id = Guid.Empty;
        return false;
    }

    private static bool TryReadGuidProperty(JsonElement record, string propertyName, out Guid id)
    {
        if (record.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in record.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(property.Value.GetString(), out id))
                {
                    return true;
                }

                break;
            }
        }

        id = Guid.Empty;
        return false;
    }
}
