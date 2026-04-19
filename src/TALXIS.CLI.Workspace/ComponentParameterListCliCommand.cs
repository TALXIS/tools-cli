using System.Text;
using DotMake.CommandLine;
using Microsoft.TemplateEngine.Abstractions;
using TALXIS.CLI.Shared;
using TALXIS.CLI.Workspace.TemplateEngine;

namespace TALXIS.CLI.Workspace;

/// <summary>
/// CLI command to list parameters required for a specific component template.
/// </summary>
[CliCommand(
    Description = "Lists parameters for a specific component",
    Name = "list"
)]
public class ComponentParameterListCliCommand
{
    [CliArgument(Description = "Short name of the component.")]
    public required string ShortName { get; set; }

    public int Run()
    {
        using var scaffolder = new TemplateInvoker();
        var parameters = scaffolder.ListParametersForTemplateAsync(ShortName).GetAwaiter().GetResult();
        if (parameters == null || parameters.Count == 0)
        {
            OutputWriter.WriteLine($"No parameters found for template {ShortName}");
            return 0;
        }
        OutputWriter.WriteLine($"Parameters for template {ShortName}:");

        // Always show the mandatory output parameter first
        OutputWriter.WriteLine($"--output (Project folder path)  (text)  <required>");
        OutputWriter.WriteLine("    Specifies the target project folder path where the component will be created.");
        OutputWriter.WriteLine("    Required for both creating new projects (solution, plugin, PCF, etc.) and adding components to existing projects.");
        OutputWriter.WriteLine("    Format: \"src\\Solutions.DataModel\"");

        foreach (var p in parameters)
        {
            var sb = new StringBuilder();
            sb.Append($"--{p.Name}");
            if (!string.IsNullOrEmpty(p.DisplayName))
                sb.Append($" ({p.DisplayName})");
            sb.Append($"  ({p.DataType})");
            if (!string.IsNullOrEmpty(p.DefaultValue?.ToString()))
                sb.Append($"  [default: {p.DefaultValue}]");
            if (p.Precedence.IsRequired ||
                p.Precedence.PrecedenceDefinition == Microsoft.TemplateEngine.Abstractions.PrecedenceDefinition.Required)
                sb.Append("  <required>");
            if (p.Choices != null && p.Choices.Count > 0)
            {
                var list = string.Join(", ", p.Choices.Keys);
                sb.Append($"  choices: {list}");
            }
            if (!string.IsNullOrEmpty(p.Description))
            {
                OutputWriter.WriteLine(sb.ToString());
                OutputWriter.WriteLine($"    {p.Description}");
            }
            else
            {
                OutputWriter.WriteLine(sb.ToString());
            }
        }
        return 0;
    }
}
