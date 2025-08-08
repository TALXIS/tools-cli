using DotMake.CommandLine;
using TALXIS.CLI.Workspace.TemplateEngine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Explains a solution component type. Use names returned by 'component type list' command",
    Name = "explain"
)]
public class ComponentTypeExplainCliCommand
{
    [CliArgument(Description = "Type of the component to explain")]
    public required string Type { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            Console.WriteLine("Please provide a component type");
            return 1;
        }

        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        var template = templates?.FirstOrDefault(t => string.Equals(t.Name, Type, StringComparison.OrdinalIgnoreCase)
            || t.ShortNameList.Any(sn => string.Equals(sn, Type, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            Console.WriteLine($"Component template '{Type}' not found.");
            return 1;
        }

        Console.WriteLine($"Name: {template.Name}");
        Console.WriteLine($"Short names: {string.Join(", ", template.ShortNameList)}");
        Console.WriteLine($"Description: {template.Description}");
        return 0;
    }
}
