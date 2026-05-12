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
    Description = "Lists scaffolding parameters for a specific component type. Shows parameter names, types, defaults, and descriptions.",
    Name = "list"
)]
public class ComponentParameterListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentParameterListCliCommand));

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
        if (parameters == null || parameters.Count == 0)
        {
            OutputFormatter.WriteList(Array.Empty<object>(), _ =>
                OutputWriter.WriteLine($"No parameters found for template {ShortName}"));
            return ExitSuccess;
        }

        // Build the mandatory --output parameter plus all template parameters
        var projected = new List<object>();

        // Always include the mandatory output parameter first
        projected.Add(new
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
        });

        foreach (var p in parameters)
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
        });

        return ExitSuccess;
    }
}
