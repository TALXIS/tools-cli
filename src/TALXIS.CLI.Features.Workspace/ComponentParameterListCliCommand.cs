using System.Text;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Features.Workspace.TemplateEngine;

namespace TALXIS.CLI.Features.Workspace;

/// <summary>
/// CLI command to list parameters required for a specific component template.
/// </summary>
[CliReadOnly]
[CliCommand(
    Description = "Lists scaffolding parameters for a specific component type. Shows parameter names, types, defaults, and descriptions. " +
                  "The first row is always 'output' (the --output option), then 'name' (set with --param name=<value>) unless the template has its own 'Name' symbol, then the template's own parameters.",
    Name = "list"
)]
public class ComponentParameterListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentParameterListCliCommand));

    internal const string NameParameter = "name";

    [CliArgument(Description = "Component type name, alias, template short name, or integer code (e.g. 'Entity', 'Table', 'pp-entity', '1').")]
    public required string ShortName { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        using var scaffolder = new TemplateInvoker();

        // Resolve the user's input to a template short name via shared TemplateResolver
        var templates = await scaffolder.ListTemplatesAsync();
        var resolved = TemplateEngine.TemplateResolver.Resolve(ShortName, templates);
        var resolvedShortName = resolved?.ShortNameList.FirstOrDefault() ?? ShortName;

        var parameters = await scaffolder.ListParametersForTemplateAsync(resolvedShortName);
        var projected = ProjectParameters(parameters);
        var hasOwnParameters = parameters.Any(p => p.Name != NameParameter);
        var noOwnParametersHint = ListsNameRow(parameters)
            ? "pass only --output and optionally --param name=<value>."
            : "pass only --output.";

        var readOnlyProjected = projected.AsReadOnly();

        OutputFormatter.WriteList(readOnlyProjected, items =>
        {
            OutputWriter.WriteLine($"Parameters for template {ShortName}:");

            foreach (var item in items)
            {
                // Use dynamic to access anonymous type properties
                dynamic param = item;
                var sb = new StringBuilder();
                sb.Append($"--{param.name}");
                if (!string.IsNullOrEmpty((string?)param.displayName))
                    sb.Append($" ({param.displayName})");
                sb.Append($"  ({param.dataType})");
                if (!string.IsNullOrEmpty((string?)param.defaultValue))
                    sb.Append($"  [default: {param.defaultValue}]");
                if ((bool)param.required)
                    sb.Append("  <required>");
                if (!string.IsNullOrEmpty((string?)param.choices))
                    sb.Append($"  choices: {param.choices}");
                OutputWriter.WriteLine(sb.ToString());
                if (!string.IsNullOrEmpty((string?)param.description))
                    OutputWriter.WriteLine($"    {param.description}");
            }

            if (!hasOwnParameters)
                OutputWriter.WriteLine($"Template {ShortName} declares no parameters of its own; {noOwnParametersHint}");
        });

        return ExitSuccess;
    }

    /// <summary>
    /// Builds the rows in order: the 'output' option, then 'name' when the template declares it, then the template's own parameters.
    /// </summary>
    internal static List<object> ProjectParameters(IReadOnlyList<ITemplateParameter> parameters)
    {
        var projected = new List<object>
        {
            new
            {
                name = "output",
                displayName = "Project folder path",
                dataType = "text",
                defaultValue = (string?)null,
                required = true,
                choices = (string?)null,
                description = "Specifies the target project folder path where the component will be created. " +
                              "Required for both creating new projects (solution, plugin, PCF, etc.) and adding components to existing projects. " +
                              "Format: \"src\\Solutions.DataModel\""
            }
        };

        // 'name' has no symbol of its own in most templates; it replaces the sourceName, and create falls back to the output folder name
        if (ListsNameRow(parameters))
        {
            projected.Add(new
            {
                name = NameParameter,
                displayName = "Name",
                dataType = "text",
                defaultValue = (string?)null,
                required = false,
                choices = (string?)null,
                description = "Name of the created item or project (replaces the template's placeholder name). Defaults to the output folder name."
            });
        }

        foreach (var p in parameters.Where(p => p.Name != NameParameter))
        {
            var isRequired = p.Precedence.IsRequired ||
                p.Precedence.PrecedenceDefinition == PrecedenceDefinition.Required;
            var choiceList = p.Choices != null && p.Choices.Count > 0
                ? string.Join(", ", p.Choices.Keys)
                : null;

            projected.Add(new
            {
                name = p.Name,
                displayName = p.DisplayName,
                dataType = p.DataType,
                defaultValue = p.DefaultValue?.ToString(),
                required = isRequired,
                choices = choiceList,
                description = p.Description
            });
        }

        return projected;
    }

    /// <summary>
    /// True when the template declares 'name' and no own symbol (such as 'Name') shares the case-insensitive --param key.
    /// </summary>
    private static bool ListsNameRow(IReadOnlyList<ITemplateParameter> parameters) =>
        parameters.Any(p => p.Name == NameParameter) &&
        !parameters.Any(p => p.Name != NameParameter && string.Equals(p.Name, NameParameter, StringComparison.OrdinalIgnoreCase));
}
