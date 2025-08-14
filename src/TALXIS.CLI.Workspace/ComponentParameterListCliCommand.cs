using DotMake.CommandLine;
using Microsoft.TemplateEngine.Abstractions;
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
            Console.WriteLine($"No parameters found for template '{ShortName}'.");
            return 0;
        }
        Console.WriteLine($"Parameters for template '{ShortName}':");
        
        // Always show the mandatory output parameter first
        Console.WriteLine("--output (Project folder path)  (text)  <required>");
        Console.WriteLine("    Specifies the target project folder path where the component will be created.");
        Console.WriteLine("    Required for both creating new projects (solution, plugin, PCF, etc.) and adding components to existing projects.");
        Console.WriteLine("    Format: \"src\\Solutions.DataModel\"");
        
        foreach (var p in parameters)
        {
            Console.Write($"--{p.Name}");
            if (!string.IsNullOrEmpty(p.DisplayName))
                Console.Write($" ({p.DisplayName})");
            Console.Write($"  ({p.DataType})");
            if (!string.IsNullOrEmpty(p.DefaultValue?.ToString()))
                Console.Write($"  [default: {p.DefaultValue}]");
            // Check if parameter is required
            if (p.Precedence.IsRequired || 
                p.Precedence.PrecedenceDefinition == Microsoft.TemplateEngine.Abstractions.PrecedenceDefinition.Required)
                Console.Write("  <required>");
            if (p.Choices != null && p.Choices.Count > 0)
            {
                var list = string.Join(", ", p.Choices.Keys);
                Console.Write($"  choices: {list}");
            }
            Console.WriteLine();
            if (!string.IsNullOrEmpty(p.Description))
                Console.WriteLine($"    {p.Description}");
        }
        return 0;
    }
}
