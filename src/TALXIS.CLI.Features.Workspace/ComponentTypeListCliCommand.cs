using DotMake.CommandLine;
using TALXIS.CLI.Shared;
using TALXIS.CLI.Features.Workspace.TemplateEngine;

namespace TALXIS.CLI.Features.Workspace;

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
            OutputWriter.WriteLine("No components available.");
            return 0;
        }
        foreach (var template in templates)
        {
            var description = string.IsNullOrWhiteSpace(template.Description) ? "" : $" - {template.Description}";
            OutputWriter.WriteLine($"- {template.ShortNameList.FirstOrDefault()}{description}");
        }
        return 0;
    }
}
