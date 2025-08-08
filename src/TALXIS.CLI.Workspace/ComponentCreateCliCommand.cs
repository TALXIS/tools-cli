using System.CommandLine.Completions;
using DotMake.CommandLine;
using TALXIS.CLI.Workspace.TemplateEngine;
namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Scaffolds a component from a template and passes parameters",
    Name = "create")]
public class ComponentCreateCliCommand : ICliGetCompletions
{
    [CliArgument(Description = "Type of the component (e.g. 'pp-entity')")]
    public required string Type { get; set; }

    [CliOption(Name = "Output", Aliases = ["-o"], Description = "Output path for the new component")]
    public required string OutputPath { get; set; }

    // Conflicts with OutputPath in our templates
    // [CliOption(Name = "name", Aliases = ["-n"], Description = "The name for the created output. If not specified, the name of the output directory is used.", Required = false)]
    // public string? Name { get; set; }

    [CliOption(Description = "Component parameters which can be retrieved by parameter list command. Inputs need to be passed in the form key=value. Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    public async Task<int> RunAsync()
    {
        
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // Parse template-specific parameters
        foreach (var p in Param)
        {
            var idx = p.IndexOf('=');
            if (idx <= 0 || idx == p.Length - 1)
            {
                throw new ArgumentException($"Invalid parameter format: '{p}'. Use key=value.");
            }
            var key = p.Substring(0, idx);
            var value = p.Substring(idx + 1);
            parameters[key] = value;
        }
        using var scaffolder = new TemplateInvoker();
        try
        {
            var (success, failedActions) = await scaffolder.ScaffoldAsync(Type, OutputPath, parameters);
            if (success)
            {
                Console.WriteLine($"Component scaffolded to '{OutputPath}' using template '{Type}'.");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"Component scaffolded, but one or more post-actions failed. See errors above.");
                if (failedActions.Count > 0)
                {
                    Console.Error.WriteLine("\nSummary of failed post-actions:");
                    foreach (var failed in failedActions)
                    {
                        Console.Error.WriteLine($"- {failed.Description ?? failed.ActionId.ToString()}");
                    }
                }
                return 1;
            }
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Parameter validation failed"))
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("parameter") || ex.Message.Contains("Creation failed"))
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public IEnumerable<CompletionItem> GetCompletions(string propertyName, CompletionContext completionContext)
    {
        switch (propertyName)
        {
            case nameof(Type):
                try
                {
                    using var scaffolder = new TemplateInvoker();
                    var templates = scaffolder.ListTemplatesAsync().Result;
                    if (templates != null)
                    {
                        return templates.Select(t => new CompletionItem(t.ShortNameList.FirstOrDefault() ?? t.Name));
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error fetching completions: {ex.Message}");
                }
                break;
        }

        return Enumerable.Empty<CompletionItem>();
    }
}
