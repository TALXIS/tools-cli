using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

/// <summary>
/// CLI command to list available templates in the ComponentScaffolder.
/// </summary>
[CliCommand(
    Description = "Lists available component templates",
    Name = "list"
)]
public class ComponentListCliCommand
{
    public async Task<int> RunAsync()
    {
        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        if (templates == null || !templates.Any())
        {
            Console.WriteLine("No templates available.");
            return 0;
        }
        Console.WriteLine("Available templates:");
        foreach (var template in templates)
        {
            var description = string.IsNullOrWhiteSpace(template.Description) ? "" : $" - {template.Description}";
            Console.WriteLine($"- {template.Name} (short name: {string.Join(", ", template.ShortNameList)}){description}");
        }
        return 0;
    }
}
