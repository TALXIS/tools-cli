using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

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
        foreach (var p in parameters)
        {
            Console.Write($"--{p.Name}");
            Console.Write($"  ({p.DataType})");
            if (!string.IsNullOrEmpty(p.DefaultValue?.ToString()))
                Console.Write($"  [default: {p.DefaultValue}]");
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
        return 0;
    }
}
