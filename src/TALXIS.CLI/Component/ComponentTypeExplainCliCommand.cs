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

        // Resolve template using shared TemplateResolver — handles short names, registry names, aliases, type codes.
        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        var template = templates != null ? TemplateResolver.Resolve(Type, templates) : null;

        if (template != null)
        {
            templateDescription = template.Description;
            templateShortName = template.ShortNameList.FirstOrDefault();

            // If registry lookup failed but template has a componentType tag, resolve from that
            if (def == null)
            {
                var taggedType = TemplateResolver.GetComponentTypeName(template);
                if (taggedType != null)
                    def = ComponentDefinitionRegistry.GetByName(taggedType);
            }
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
            identity = def.Identity,
            hasParent = def.HasParent,
            rootComponent = def.HasParent ? def.RootComponent : (int?)null,
            isCustomizable = def.IsCustomizable,
            canBeDeleted = def.CanBeDeleted,
        };

        OutputFormatter.WriteData(result, _ => PrintExplanation(def, templateDescription, templateShortName));

        return ExitSuccess;
    }

    // Text-renderer callback — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintExplanation(ComponentDefinition d, string? description, string? templateShortName)
    {
        const int labelWidth = -22;

        OutputWriter.WriteLine($"{"Name:",labelWidth}{d.Name}");
        OutputWriter.WriteLine($"{"Type Code:",labelWidth}{(int)d.TypeCode}");
        if (d.Aliases is { Count: > 0 })
            OutputWriter.WriteLine($"{"Aliases:",labelWidth}{string.Join(", ", d.Aliases)}");
        OutputWriter.WriteLine($"{"Identity:",labelWidth}{d.Identity}");

        if (!string.IsNullOrWhiteSpace(description))
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine($"Description:");
            OutputWriter.WriteLine($"  {description}");
        }

        if (!string.IsNullOrWhiteSpace(templateShortName))
            OutputWriter.WriteLine($"\n{"Template:",labelWidth}{templateShortName}");

        if (d.IsCustomizable || d.CanBeDeleted)
        {
            OutputWriter.WriteLine();
            if (d.IsCustomizable) OutputWriter.WriteLine($"{"Customizable:",labelWidth}true");
            if (d.CanBeDeleted) OutputWriter.WriteLine($"{"Can Be Deleted:",labelWidth}true");
        }

        if (d.HasParent)
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine($"{"Parent Type Code:",labelWidth}{d.RootComponent}");
        }
    }
#pragma warning restore TXC003
}
