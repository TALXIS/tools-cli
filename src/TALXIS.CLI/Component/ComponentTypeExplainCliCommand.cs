using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Component;

/// <summary>
/// Explains a specific component type — shows description (from template), metadata (from registry),
/// and scaffolding availability. Accepts canonical name, alias, enum name, template short name, or integer type code.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "explain",
    Description = "Show detailed information about a component type — description, metadata, identity strategy, parent-child relationships, and scaffolding availability."
)]
public class ComponentTypeExplainCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<ComponentTypeExplainCliCommand>();

    [CliArgument(Description = "Component type name, alias, template short name, or integer code (e.g. 'Entity', 'Table', 'pp-entity', '1').")]
    public string Type { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            Logger.LogError("Component type is required. Run 'txc component type list' to see available types.");
            return ExitValidationError;
        }

        // Try registry first, then template lookup
        var def = ComponentDefinitionRegistry.GetByName(Type);
        string? templateDescription = null;
        string? templateShortName = null;

        // Look up matching template for description
        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        var template = templates?.FirstOrDefault(t =>
            string.Equals(t.Name, Type, StringComparison.OrdinalIgnoreCase)
            || t.ShortNameList.Any(sn => string.Equals(sn, Type, StringComparison.OrdinalIgnoreCase))
            // Also match by registry name/alias (e.g. "Entity" → "pp-entity")
            || (def != null && t.ShortNameList.Any(sn =>
                sn.EndsWith(def.Name, StringComparison.OrdinalIgnoreCase)
                || (def.Aliases?.Any(a => sn.EndsWith(a, StringComparison.OrdinalIgnoreCase)) == true))));

        if (template != null)
        {
            templateDescription = template.Description;
            templateShortName = template.ShortNameList.FirstOrDefault();
        }

        // If no registry definition found, try to resolve from template name
        if (def is null && templateShortName != null)
        {
            // Template exists but no ComponentDefinition — show template info only
            var data = new
            {
                templateShortName,
                description = templateDescription
            };

            OutputFormatter.WriteData(data, d =>
            {
                OutputWriter.WriteLine($"Template: {d.templateShortName}");
                if (!string.IsNullOrWhiteSpace(d.description))
                    OutputWriter.WriteLine($"Description: {d.description}");
            });
            return ExitSuccess;
        }

        if (def is null)
        {
            Logger.LogError("Unknown component type '{Type}'. Run 'txc component type list' to see available types.", Type);
            return ExitValidationError;
        }

        var result = new
        {
            typeCode = (int)def.TypeCode,
            name = def.Name,
            aliases = def.Aliases ?? (IReadOnlyList<string>)Array.Empty<string>(),
            description = templateDescription,
            templateShortName,
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

        OutputFormatter.WriteData(result, _ => PrintExplanation(def, templateDescription, templateShortName));

        return ExitSuccess;
    }

    // Text-renderer callback — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintExplanation(ComponentDefinition d, string? description, string? templateShortName)
    {
        const int labelWidth = -28;

        OutputWriter.WriteLine($"{"Name:",labelWidth}{d.Name}");
        OutputWriter.WriteLine($"{"Type Code:",labelWidth}{(int)d.TypeCode}");
        if (d.Aliases is { Count: > 0 })
            OutputWriter.WriteLine($"{"Aliases:",labelWidth}{string.Join(", ", d.Aliases)}");

        if (!string.IsNullOrWhiteSpace(description))
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine($"Description:");
            OutputWriter.WriteLine($"  {description}");
        }

        if (!string.IsNullOrWhiteSpace(templateShortName))
            OutputWriter.WriteLine($"\n{"Scaffolding Template:",labelWidth}{templateShortName}");

        OutputWriter.WriteLine();
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
