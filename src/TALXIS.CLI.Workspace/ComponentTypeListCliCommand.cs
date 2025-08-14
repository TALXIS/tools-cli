using DotMake.CommandLine;
using TALXIS.CLI.Workspace.TemplateEngine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Lists available solution components",
    Name = "list"
)]
public class ComponentTypeListCliCommand
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
        foreach (var template in templates)
        {
            var description = string.IsNullOrWhiteSpace(template.Description) ? "" : $" - {template.Description}";
            Console.WriteLine($"- {template.ShortNameList.FirstOrDefault()}{description}");
        }
        return 0;
    }
}
