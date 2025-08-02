using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

[CliCommand(
    Description = "Lists available solution components",
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
            Console.WriteLine("No components available.");
            return 0;
        }
        Console.WriteLine("Available components:");
        foreach (var template in templates)
        {
            var description = string.IsNullOrWhiteSpace(template.Description) ? "" : $" - {template.Description}";
            Console.WriteLine($"- {template.Name} (short name: {string.Join(", ", template.ShortNameList)}){description}");
        }
        return 0;
    }
}
