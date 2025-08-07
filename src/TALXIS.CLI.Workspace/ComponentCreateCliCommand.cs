using System.CommandLine.Completions;
using DotMake.CommandLine;
using TALXIS.CLI.Workspace.TemplateEngine;
namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Scaffolds a component from a template and passes parameters",
    Name = "create")]
public class ComponentCreateCliCommand : ICliGetCompletions
{
    [CliArgument(Description = "Short name of the component")]
    public required string ShortName { get; set; }

    [CliOption(Name = "output", Aliases = ["-o"], Description = "Output path for the scaffolded component")]
    public string OutputPath { get; set; } = string.Empty;

    [CliOption(Name = "name", Aliases = ["-n"], Description = "The name for the created output. If not specified, the name of the output directory is used.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Description = "Component parameters which can be retrieved by parameter list command. Inputs need to be passed in the form key=value. Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    public async Task<int> RunAsync()
    {
        // Validate that at least output path or name is provided
        if (string.IsNullOrWhiteSpace(OutputPath) && string.IsNullOrWhiteSpace(Name))
        {
            Console.Error.WriteLine("Error: Either --output or --name must be specified.");
            Console.Error.WriteLine("Use --help for more information.");
            return 1;
        }
        
        // If name is provided but no output path, use current directory
        var resolvedOutputPath = OutputPath;
        if (string.IsNullOrWhiteSpace(resolvedOutputPath))
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                resolvedOutputPath = Name; // Use name as the directory name in current location
            }
            else
            {
                // This shouldn't happen due to validation above, but just in case
                Console.Error.WriteLine("Error: Output path could not be determined.");
                return 1;
            }
        }
        
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
        
        // Always add the name parameter if provided
        // The template engine will handle preferNameDirectory behavior correctly
        if (!string.IsNullOrEmpty(Name))
        {
            parameters["name"] = Name;
        }

        using var scaffolder = new TemplateInvoker();
        try
        {
            var (success, failedActions) = await scaffolder.ScaffoldAsync(ShortName, resolvedOutputPath, parameters);
            if (success)
            {
                Console.WriteLine($"Component scaffolded to '{resolvedOutputPath}' using template '{ShortName}'.");
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
            case nameof(ShortName):
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
