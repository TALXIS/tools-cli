using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>A form on the explored entity (name + form type display name).</summary>
public sealed record EntityExploreForm(string Name, string Type);

/// <summary>Aggregated single-view snapshot of an entity returned by <c>entity explore</c>.</summary>
public sealed record EntityExploreResult(
    EntityDetailRecord Entity,
    IReadOnlyList<EntityAttributeRecord> Columns,
    int CustomColumnCount,
    int SystemColumnCount,
    IReadOnlyList<EntityRelationshipRecord> Relationships,
    long? RecordCount,
    IReadOnlyList<EntityExploreForm> Forms,
    int? ViewCount);

/// <summary>
/// <c>txc environment entity explore</c> - columns with option set values,
/// relationships, and record/form/view counts in one view.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "explore",
    Description = "Shows a Dataverse table at a glance from the LIVE connected environment: columns with option set values expanded inline, relationships, and record/form/view counts. Requires an active profile. Use instead of separate describe/optionset/relationship calls when you want the full picture of a table."
)]
public class EntityExploreCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityExploreCliCommand));

    [CliArgument(Name = "entity", Description = "The logical name of the entity to explore.")]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--columns-only", Description = "Skip relationships and record/form/view counts.", Required = false)]
    public bool ColumnsOnly { get; set; }

    [CliOption(Name = "--include-system", Description = "Show system columns in the table (the summary counts always include them).", Required = false)]
    public bool IncludeSystem { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var metadata = TxcServices.Get<IDataverseEntityMetadataService>();

        var detail = await metadata.GetEntityDetailAsync(Profile, Entity, CancellationToken.None).ConfigureAwait(false);
        var allColumns = await metadata.DescribeEntityAsync(Profile, Entity, includeSystem: true, CancellationToken.None).ConfigureAwait(false);

        int customColumnCount = allColumns.Count(column => column.IsCustomAttribute);
        int systemColumnCount = allColumns.Count - customColumnCount;
        var columns = IncludeSystem ? allColumns : allColumns.Where(column => column.IsCustomAttribute).ToList();

        IReadOnlyList<EntityRelationshipRecord> relationships = Array.Empty<EntityRelationshipRecord>();
        long? recordCount = null;
        IReadOnlyList<EntityExploreForm> forms = Array.Empty<EntityExploreForm>();
        int? viewCount = null;

        if (!ColumnsOnly)
        {
            relationships = await metadata.ListRelationshipsAsync(Profile, Entity, CancellationToken.None).ConfigureAwait(false);

            var query = TxcServices.Get<IDataverseQueryService>();
            recordCount = await TryCountRecordsAsync(query, detail).ConfigureAwait(false);
            forms = await TryListFormsAsync(query).ConfigureAwait(false);
            viewCount = await TryCountViewsAsync(query).ConfigureAwait(false);
        }

        var result = new EntityExploreResult(
            detail, columns, customColumnCount, systemColumnCount,
            relationships, recordCount, forms, viewCount);

        OutputFormatter.WriteData(result, Print);
        return ExitSuccess;
    }

    private async Task<long?> TryCountRecordsAsync(IDataverseQueryService query, EntityDetailRecord detail)
    {
        var primaryId = detail.PrimaryIdAttribute ?? $"{Entity}id";
        var fetchXml =
            $"<fetch aggregate='true'><entity name='{Entity}'>" +
            $"<attribute name='{primaryId}' alias='recordcount' aggregate='count'/>" +
            "</entity></fetch>";
        try
        {
            var result = await query.QueryFetchXmlAsync(Profile, fetchXml, top: null, includeAnnotations: false, CancellationToken.None).ConfigureAwait(false);
            if (result.Records.Count > 0 && result.Records[0].TryGetProperty("recordcount", out var countElement))
            {
                return countElement.GetInt64();
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Record count unavailable: {Message}", ex.Message);

            return null;
        }
    }

    private async Task<IReadOnlyList<EntityExploreForm>> TryListFormsAsync(IDataverseQueryService query)
    {
        try
        {
            var result = await query.QueryODataAsync(
                Profile, "systemforms",
                select: "name,type",
                filter: $"objecttypecode eq '{Entity}'",
                orderBy: "name", top: null, includeAnnotations: false, CancellationToken.None).ConfigureAwait(false);

            return result.Records
                .Select(record => new EntityExploreForm(
                    record.TryGetProperty("name", out var name) ? name.GetString() ?? "?" : "?",
                    record.TryGetProperty("type", out var type) && type.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? EntityExploreHelpers.FormTypeName(type.GetInt32())
                        : "?"))
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Form list unavailable: {Message}", ex.Message);
            return Array.Empty<EntityExploreForm>();
        }
    }

    private async Task<int?> TryCountViewsAsync(IDataverseQueryService query)
    {
        try
        {
            var result = await query.QueryODataAsync(
                Profile, "savedqueries",
                select: "savedqueryid",
                filter: $"returnedtypecode eq '{Entity}'",
                orderBy: null, top: null, includeAnnotations: false, CancellationToken.None).ConfigureAwait(false);
            return result.Records.Count;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("View count unavailable: {Message}", ex.Message);
            return null;
        }
    }

#pragma warning disable TXC003
    private static void Print(EntityExploreResult result)
    {
        var title = result.Entity.DisplayName is { } displayName
            ? $"{result.Entity.LogicalName} - {displayName}"
            : result.Entity.LogicalName;
        OutputWriter.WriteLine(title);
        OutputWriter.WriteLine(new string('=', title.Length));
        OutputWriter.WriteLine("");

        PrintColumns(result);

        if (result.Relationships.Count > 0)
        {
            OutputWriter.WriteLine("");
            PrintRelationships(result);
        }

        var footer = BuildFooter(result);
        if (footer.Length > 0)
        {
            OutputWriter.WriteLine("");
            OutputWriter.WriteLine(footer);
        }
    }

    private static void PrintColumns(EntityExploreResult result)
    {
        OutputWriter.WriteLine($"Columns ({result.CustomColumnCount} custom + {result.SystemColumnCount} system)");
        if (result.Columns.Count == 0)
        {
            OutputWriter.WriteLine("No columns to show.");
            return;
        }

        int logicalWidth = Math.Clamp(result.Columns.Max(column => column.LogicalName.Length), 12, 48);
        int typeWidth = Math.Clamp(result.Columns.Max(column => column.AttributeTypeName.Length), 4, 30);
        int displayWidth = Math.Clamp(result.Columns.Max(column => (column.DisplayName ?? "").Length), 12, 40);
        int requiredWidth = 11;

        string header =
            $"{"Logical Name".PadRight(logicalWidth)} | " +
            $"{"Type".PadRight(typeWidth)} | " +
            $"{"Display Name".PadRight(displayWidth)} | " +
            $"{"Required".PadRight(requiredWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var column in result.Columns)
        {
            string line =
                $"{Truncate(column.LogicalName, logicalWidth).PadRight(logicalWidth)} | " +
                $"{Truncate(column.AttributeTypeName, typeWidth).PadRight(typeWidth)} | " +
                $"{Truncate(column.DisplayName ?? "", displayWidth).PadRight(displayWidth)} | " +
                $"{EntityExploreHelpers.RequiredDisplay(column.RequiredLevel).PadRight(requiredWidth)}";
            OutputWriter.WriteLine(line);

            foreach (var optionLine in EntityExploreHelpers.OptionLines(column.OptionValues))
            {
                OutputWriter.WriteLine($"  └─ {optionLine}");
            }
        }
    }

    private static void PrintRelationships(EntityExploreResult result)
    {
        OutputWriter.WriteLine($"Relationships ({result.Relationships.Count})");

        int nameWidth = Math.Clamp(result.Relationships.Max(relationship => relationship.SchemaName.Length), 12, 50);
        int typeWidth = 4;

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Type".PadRight(typeWidth)} | " +
            "Related Entity";

        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length + 16));

        foreach (var relationship in result.Relationships)
        {
            string related = EntityExploreHelpers.RelatedEntity(relationship, result.Entity.LogicalName);
            string line =
                $"{Truncate(relationship.SchemaName, nameWidth).PadRight(nameWidth)} | " +
                $"{EntityExploreHelpers.RelationshipTypeShort(relationship.RelationshipType).PadRight(typeWidth)} | " +
                related;

            OutputWriter.WriteLine(line);
        }
    }

    private static string BuildFooter(EntityExploreResult result)
    {
        var parts = new List<string>();

        if (result.RecordCount is { } recordCount) parts.Add($"Records: {recordCount}");

        if (result.Forms.Count > 0)
        {
            var types = string.Join(", ", result.Forms.Select(form => form.Type).Distinct());
            parts.Add($"Forms: {result.Forms.Count} ({types})");
        }

        if (result.ViewCount is { } viewCount) parts.Add($"Views: {viewCount}");

        return string.Join(" | ", parts);
    }
#pragma warning restore TXC003

    private static string Truncate(string value, int maxWidth) => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
