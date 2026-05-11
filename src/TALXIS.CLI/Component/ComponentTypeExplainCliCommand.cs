using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Component;

/// <summary>
/// Explains a specific component type — shows full <see cref="ComponentDefinition"/> details.
/// Accepts canonical name, alias, enum name, or integer type code.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "explain",
    Description = "Show detailed information about a component type."
)]
public class ComponentTypeExplainCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<ComponentTypeExplainCliCommand>();

    [CliArgument(Description = "Component type name, alias, or integer code (e.g. 'Entity', 'Table', '1').")]
    public string Type { get; set; } = null!;

    protected override Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            Logger.LogError("Component type is required. Run 'txc component type list' to see available types.");
            return Task.FromResult(ExitValidationError);
        }

        var def = ComponentDefinitionRegistry.GetByName(Type);
        if (def is null)
        {
            Logger.LogError("Unknown component type '{Type}'. Run 'txc component type list' to see available types.", Type);
            return Task.FromResult(ExitValidationError);
        }

        var data = new
        {
            typeCode = (int)def.TypeCode,
            name = def.Name,
            aliases = def.Aliases ?? (IReadOnlyList<string>)Array.Empty<string>(),
            serializedName = def.SerializedName,
            directory = def.Directory,
            filePattern = def.FilePattern,
            identity = def.Identity,
            supportsMerge = def.SupportsMerge,
            isMergeable = def.IsMergeable,
            isFileBacked = def.IsFileBacked,
            hasSubfolders = def.HasSubfolders,
            hasParent = def.HasParent,
            rootComponent = def.RootComponent,
            isCustomizable = def.IsCustomizable,
            canBeDeleted = def.CanBeDeleted,
            primaryKeyName = def.PrimaryKeyName,
            exportKeyAttributes = def.ExportKeyAttributes
        };

        OutputFormatter.WriteData(data, _ => PrintExplanation(def));

        return Task.FromResult(ExitSuccess);
    }

#pragma warning disable TXC003
    private static void PrintExplanation(ComponentDefinition d)
    {
        const int labelWidth = -28;

        OutputWriter.WriteLine($"{"Name:",labelWidth}{d.Name}");
        OutputWriter.WriteLine($"{"Type Code:",labelWidth}{(int)d.TypeCode}");
        if (d.Aliases is { Count: > 0 })
            OutputWriter.WriteLine($"{"Aliases:",labelWidth}{string.Join(", ", d.Aliases)}");
        OutputWriter.WriteLine($"{"Serialized Name:",labelWidth}{d.SerializedName}");
        OutputWriter.WriteLine($"{"Directory:",labelWidth}{d.Directory}");
        OutputWriter.WriteLine($"{"File Pattern:",labelWidth}{d.FilePattern}");
        OutputWriter.WriteLine($"{"Identity Strategy:",labelWidth}{d.Identity}");

        OutputWriter.WriteLine();
        OutputWriter.WriteLine("Behavioral Flags:");
        OutputWriter.WriteLine($"  {"Supports Merge:",labelWidth}{BoolStr(d.SupportsMerge)}");
        OutputWriter.WriteLine($"  {"Is Mergeable:",labelWidth}{BoolStr(d.IsMergeable)}");
        OutputWriter.WriteLine($"  {"Is File-Backed:",labelWidth}{BoolStr(d.IsFileBacked)}");
        OutputWriter.WriteLine($"  {"Has Subfolders:",labelWidth}{BoolStr(d.HasSubfolders)}");
        OutputWriter.WriteLine($"  {"Is Customizable:",labelWidth}{BoolStr(d.IsCustomizable)}");
        OutputWriter.WriteLine($"  {"Can Be Deleted:",labelWidth}{BoolStr(d.CanBeDeleted)}");

        if (d.HasParent)
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine("Parent-Child:");
            OutputWriter.WriteLine($"  {"Has Parent:",labelWidth}true");
            OutputWriter.WriteLine($"  {"Root Component:",labelWidth}{d.RootComponent}");
            if (d.GroupParentComponentType.HasValue)
                OutputWriter.WriteLine($"  {"Group Parent Type:",labelWidth}{d.GroupParentComponentType}");
            if (!string.IsNullOrWhiteSpace(d.GroupParentComponentAttributeName))
                OutputWriter.WriteLine($"  {"Group Parent Attr:",labelWidth}{d.GroupParentComponentAttributeName}");
        }

        if (!string.IsNullOrWhiteSpace(d.PrimaryKeyName))
            OutputWriter.WriteLine($"\n{"Primary Key:",labelWidth}{d.PrimaryKeyName}");
        if (!string.IsNullOrWhiteSpace(d.ExportKeyAttributes))
            OutputWriter.WriteLine($"{"Export Key Attrs:",labelWidth}{d.ExportKeyAttributes}");
    }
#pragma warning restore TXC003

    private static string BoolStr(bool value) => value ? "true" : "false";
}
