using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Describes the columns/attributes of a specific entity.
/// Usage: <c>txc environment entity describe &lt;entity&gt; [--include-system]</c>
/// </summary>
[McpToolAnnotations(ReadOnlyHint = true)]
[CliCommand(
    Name = "describe",
    Description = "Describe columns/attributes of an entity."
)]
public class EntityDescribeCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityDescribeCliCommand));

    [CliArgument(Name = "entity", Description = "The logical name of the entity to describe.")]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--include-system", Description = "Include non-customizable system attributes in the output.", Required = false)]
    public bool IncludeSystem { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        var rows = await service.DescribeEntityAsync(Profile, Entity, IncludeSystem, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintAttributesTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintAttributesTable(IReadOnlyList<EntityAttributeRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No attributes found.");
            return;
        }

        int logicalWidth = Math.Clamp(rows.Max(r => r.LogicalName.Length), 12, 48);
        int typeWidth = Math.Clamp(rows.Max(r => r.AttributeTypeName.Length), 4, 30);
        int displayWidth = Math.Clamp(rows.Max(r => (r.DisplayName ?? "").Length), 12, 40);
        int customWidth = 6;
        int pkWidth = 2;
        int nameWidth = 4;
        int maxLenWidth = 10;

        string header =
            $"{"Logical Name".PadRight(logicalWidth)} | " +
            $"{"Type".PadRight(typeWidth)} | " +
            $"{"Display Name".PadRight(displayWidth)} | " +
            $"{"Custom".PadRight(customWidth)} | " +
            $"{"PK".PadRight(pkWidth)} | " +
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Max Length".PadRight(maxLenWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var r in rows)
        {
            string logical = Truncate(r.LogicalName, logicalWidth);
            string type = Truncate(r.AttributeTypeName, typeWidth);
            string display = Truncate(r.DisplayName ?? "", displayWidth);
            string custom = r.IsCustomAttribute ? "true" : "false";
            string pk = r.IsPrimaryId ? "*" : "";
            string name = r.IsPrimaryName ? "*" : "";
            string maxLen = r.MaxLength.HasValue ? r.MaxLength.Value.ToString() : "";

            OutputWriter.WriteLine(
                $"{logical.PadRight(logicalWidth)} | " +
                $"{type.PadRight(typeWidth)} | " +
                $"{display.PadRight(displayWidth)} | " +
                $"{custom.PadRight(customWidth)} | " +
                $"{pk.PadRight(pkWidth)} | " +
                $"{name.PadRight(nameWidth)} | " +
                $"{maxLen.PadRight(maxLenWidth)}");
        }
    }
#pragma warning restore TXC003

    /// <summary>Truncate a string to fit the column width, appending a dot if trimmed.</summary>
    private static string Truncate(string value, int maxWidth) =>
        value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;

}
