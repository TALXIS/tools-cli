using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists all relationships for a Dataverse entity.
/// Usage: <c>txc environment entity relationship list --entity &lt;name&gt; [--format json]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List relationships for an entity."
)]
#pragma warning disable TXC003
public class EntityRelationshipListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityRelationshipListCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity.", Required = true)]
    public string Entity { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        var rows = await service.ListRelationshipsAsync(Profile, Entity, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintRelationshipsTable);
        return ExitSuccess;
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
}
