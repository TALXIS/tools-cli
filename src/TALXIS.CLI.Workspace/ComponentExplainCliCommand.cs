using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Explains a solution component. Use names returned by 'list' command.",
    Name = "explain"
)]
public class ComponentExplainCliCommand
{
    [CliArgument(Description = "Name of the component to explain")]
    public string Name { get; set; } = string.Empty;

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Console.WriteLine("Please provide a component name.");
            return 1;
        }

        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        var template = templates?.FirstOrDefault(t => string.Equals(t.Name, Name, StringComparison.OrdinalIgnoreCase)
            || t.ShortNameList.Any(sn => string.Equals(sn, Name, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            Console.WriteLine($"Component template '{Name}' not found.");
            return 1;
        }

        Console.WriteLine($"Name: {template.Name}");
        Console.WriteLine($"Short names: {string.Join(", ", template.ShortNameList)}");
        Console.WriteLine($"Description: {template.Description}");
        return 0;
    }
}
