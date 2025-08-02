using System.CommandLine.Completions;
using DotMake.CommandLine;
namespace TALXIS.CLI.Component;

[CliCommand(
    Description = "Scaffolds a component from a template and passes parameters",
    Name = "create")]
public class ComponentCreateCliCommand : ICliGetCompletions
{
    [CliArgument(Description = "Short name of the component")]
    public required string ShortName { get; set; }

    [CliOption(Name = "output", Description = "Output path for the scaffolded component")]
    public string OutputPath { get; set; } = string.Empty;

    [CliOption(Description = "Component parameters which can be retrieved by parameter list command. Inputs need to be passed in the form key=value. Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    public async Task<int> RunAsync()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        var (success, failedActions) = await scaffolder.ScaffoldAsync(ShortName, OutputPath, parameters);
        Console.Error.WriteLine($"[DEBUG] TemplateScaffoldCliCommand: failedActions.Count = {failedActions.Count}");
        if (success)
        {
            Console.WriteLine($"Component scaffolded to '{OutputPath}' using template '{ShortName}'.");
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
