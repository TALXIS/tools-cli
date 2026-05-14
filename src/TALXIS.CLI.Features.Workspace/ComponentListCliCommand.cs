using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;
using TALXIS.Platform.Metadata.Serialization.Xml;

namespace TALXIS.CLI.Features.Workspace;

/// <summary>
/// Lists component instances found in the local unpacked solution workspace.
/// Reads the workspace directory using <see cref="XmlWorkspaceReader"/> and
/// enumerates all component instances with their type, name, identity, and source path.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List component instances in the local unpacked solution workspace. Shows type, name, identity, and file path for each component."
)]
public class ComponentListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<ComponentListCliCommand>();

    [CliOption(Name = "--path", Description = "Path to the solution project directory. Defaults to the current directory.", Required = false)]
    public string Path { get; set; } = ".";

    [CliOption(Name = "--type", Description = "Filter by component type — accepts canonical name (Entity), alias (Table), or integer code.", Required = false)]
    public string? Type { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        if (!Directory.Exists(fullPath))
        {
            Logger.LogError("Directory not found: {Path}", fullPath);
            return Task.FromResult(ExitError);
        }

        // Resolve optional type filter
        ComponentType? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(Type))
        {
            var def = ComponentDefinitionRegistry.GetByName(Type);
            if (def is null)
            {
                Logger.LogError("Unknown component type '{Type}'.", Type);
                return Task.FromResult(ExitValidationError);
            }
            typeFilter = def.TypeCode;
        }

        var reader = new XmlWorkspaceReader();
        var workspace = reader.Load(fullPath);

        // Report load errors as warnings
        foreach (var error in workspace.LoadErrors)
            Logger.LogWarning("Load warning: {File} — {Message}", error.FilePath, error.Message);

        var components = workspace.EnumerateLayerComponents();

        if (typeFilter.HasValue)
            components = components.Where(c => c.Type == typeFilter.Value);

        var projected = components.Select(c => new
        {
            type = c.Type.ToString(),
            objectId = c.ObjectId,
            source = c.SourceDocumentKey
        }).ToList();

        OutputFormatter.WriteList(projected, items => PrintTable(items));

        return Task.FromResult(ExitSuccess);
    }

    // Text-renderer callback — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable<T>(IReadOnlyList<T> items) where T : notnull
    {
        var rows = items.Cast<dynamic>().ToList();
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No components found.");
            return;
        }

        int typeWidth = Math.Clamp(rows.Max(r => ((string)r.type).Length), 10, 30);
        int idWidth = Math.Clamp(rows.Max(r => ((string)r.objectId).Length), 15, 42);

        string header = $"{"Type".PadRight(typeWidth)} | {"Identity".PadRight(idWidth)} | Source";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length + 20));
        foreach (var r in rows)
        {
            OutputWriter.WriteLine(
                $"{((string)r.type).PadRight(typeWidth)} | " +
                $"{((string)r.objectId).PadRight(idWidth)} | " +
                $"{(string)r.source}");
        }
        OutputWriter.WriteLine($"\n{rows.Count} component(s).");
    }
#pragma warning restore TXC003
}
