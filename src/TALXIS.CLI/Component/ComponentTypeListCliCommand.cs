using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Component;

/// <summary>
/// Lists all known component types from the <see cref="ComponentDefinitionRegistry"/>.
/// Shows type code, canonical name, aliases, identity strategy, and directory.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List all known component types with their aliases and metadata."
)]
public class ComponentTypeListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<ComponentTypeListCliCommand>();

    [CliOption(Name = "--search", Description = "Filter types by substring match on name or alias.", Required = false)]
    public string? Search { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var allDefs = ComponentDefinitionRegistry.GetAll()
            .OrderBy(d => (int)d.TypeCode)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            allDefs = allDefs.Where(d =>
                d.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                (d.Aliases?.Any(a => a.Contains(Search, StringComparison.OrdinalIgnoreCase)) == true))
                .ToList();
        }

        var projected = allDefs.Select(d => new
        {
            typeCode = (int)d.TypeCode,
            name = d.Name,
            aliases = d.Aliases ?? (IReadOnlyList<string>)Array.Empty<string>(),
            identity = d.Identity,
            directory = d.Directory
        }).ToList();

        OutputFormatter.WriteList(projected, items => PrintTypeTable(items));

        return Task.FromResult(ExitSuccess);
    }

#pragma warning disable TXC003
    private static void PrintTypeTable<T>(IReadOnlyList<T> items) where T : notnull
    {
        var rows = items.Cast<dynamic>().ToList();
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No component types found.");
            return;
        }

        // Format aliases as comma-separated for text display
        static string FormatAliases(dynamic aliases)
        {
            var list = (IReadOnlyList<string>)aliases;
            return list.Count > 0 ? string.Join(", ", list) : "";
        }

        int codeWidth = 6;
        int nameWidth = Math.Clamp(rows.Max(r => ((string)r.name).Length), 10, 35);
        int aliasWidth = Math.Clamp(rows.Max(r => FormatAliases(r.aliases).Length), 7, 30);
        int identityWidth = 10;

        string header = $"{"Code".PadRight(codeWidth)} | {"Name".PadRight(nameWidth)} | {"Aliases".PadRight(aliasWidth)} | {"Identity".PadRight(identityWidth)} | Directory";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            OutputWriter.WriteLine(
                $"{((int)r.typeCode).ToString().PadRight(codeWidth)} | " +
                $"{((string)r.name).PadRight(nameWidth)} | " +
                $"{FormatAliases(r.aliases).PadRight(aliasWidth)} | " +
                $"{r.identity.ToString().PadRight(identityWidth)} | " +
                $"{(string)r.directory}");
        }
        OutputWriter.WriteLine($"\n{rows.Count} component type(s).");
    }
#pragma warning restore TXC003
}
