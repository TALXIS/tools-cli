using DotMake.CommandLine;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TALXIS.CLI.Component;

/// <summary>
/// CLI command to list parameters required for a specific component template.
/// </summary>
[CliCommand(
    Description = "Lists parameters for a specific component template."
)]
public class ListTemplateParametersCliCommand
{
    [CliArgument(Description = "Short name of the template.")]
    public string ShortName { get; set; } = string.Empty;

    public async Task<int> RunAsync()
    {
        try
        {
            using var scaffolder = new ComponentScaffolder();
            var parameters = await scaffolder.ListParametersForTemplateAsync(ShortName);
            if (parameters == null || parameters.Count == 0)
            {
                Console.WriteLine($"No parameters found for template '{ShortName}'.");
                return 0;
            }
            Console.WriteLine($"Parameters for template '{ShortName}':");
            foreach (var p in parameters)
            {
                Console.Write($"--{p.Name}");
                Console.Write($"  ({p.DataType})");
                if (!string.IsNullOrEmpty(p.DefaultValue?.ToString()))
                    Console.Write($"  [default: {p.DefaultValue}]");
                // Check if parameter is required by string value
                if (p.Precedence != null && p.Precedence.ToString() == "Required")
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
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing parameters: {ex.Message}");
        }
        return 0;
    }
}
