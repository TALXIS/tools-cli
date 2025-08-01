using DotMake.CommandLine;
using System;
using System.Linq;

namespace TALXIS.CLI.Component;

/// <summary>
/// CLI command to list available templates in the ComponentScaffolder.
/// </summary>
[CliCommand(
    Description = "Lists available component templates."
)]
public class ListTemplatesCliCommand
{
    public async Task<int> RunAsync()
    {
        try
        {
            using var scaffolder = new ComponentScaffolder();
            var templates = await scaffolder.ListTemplatesAsync();
            if (templates == null || !templates.Any())
            {
                Console.WriteLine("No templates available.");
                return 0;
            }
            Console.WriteLine("Available templates:");
            foreach (var template in templates)
            {
                Console.WriteLine($"- {template.Name} (short name: {string.Join(", ", template.ShortNameList)})");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing templates: {ex.Message}");
        }
        return 0;
    }
}
