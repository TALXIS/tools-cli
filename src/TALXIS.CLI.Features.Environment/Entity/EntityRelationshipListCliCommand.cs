using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists all relationships for a Dataverse entity.
/// Usage: <c>txc environment entity relationship list --entity &lt;name&gt; [--json]</c>
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List relationships for an entity."
)]
public class EntityRelationshipListCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityRelationshipListCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        IReadOnlyList<EntityRelationshipRecord> rows;
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            rows = await service.ListRelationshipsAsync(Profile, Entity, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity relationship list failed");
            return 1;
        }

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(rows, JsonOptions));
            return 0;
        }

        PrintRelationshipsTable(rows);
        return 0;
    }

    private static void PrintRelationshipsTable(IReadOnlyList<EntityRelationshipRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No relationships found.");
            return;
        }

        int schemaWidth = Math.Clamp(rows.Max(r => r.SchemaName.Length), 11, 60);
        int typeWidth = Math.Clamp(rows.Max(r => r.RelationshipType.Length), 4, 12);
        int entity1Width = Math.Clamp(rows.Max(r => r.Entity1LogicalName.Length), 7, 48);
        int entity2Width = Math.Clamp(rows.Max(r => r.Entity2LogicalName.Length), 7, 48);
        int customWidth = 6;
        int intersectWidth = Math.Clamp(rows.Max(r => (r.IntersectEntityName ?? "").Length), 9, 48);

        string header =
            $"{"Schema Name".PadRight(schemaWidth)} | " +
            $"{"Type".PadRight(typeWidth)} | " +
            $"{"Entity1".PadRight(entity1Width)} | " +
            $"{"Entity2".PadRight(entity2Width)} | " +
            $"{"Custom".PadRight(customWidth)} | " +
            $"{"Intersect".PadRight(intersectWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var r in rows)
        {
            string schema = Truncate(r.SchemaName, schemaWidth);
            string type = Truncate(r.RelationshipType, typeWidth);
            string e1 = Truncate(r.Entity1LogicalName, entity1Width);
            string e2 = Truncate(r.Entity2LogicalName, entity2Width);
            string custom = r.IsCustomRelationship ? "true" : "false";
            string intersect = Truncate(r.IntersectEntityName ?? "", intersectWidth);

            OutputWriter.WriteLine(
                $"{schema.PadRight(schemaWidth)} | " +
                $"{type.PadRight(typeWidth)} | " +
                $"{e1.PadRight(entity1Width)} | " +
                $"{e2.PadRight(entity2Width)} | " +
                $"{custom.PadRight(customWidth)} | " +
                $"{intersect.PadRight(intersectWidth)}");
        }
    }

    /// <summary>Truncate a string to fit the column width, appending a dot if trimmed.</summary>
    private static string Truncate(string value, int maxWidth) =>
        value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
