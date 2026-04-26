using System.CommandLine.Completions;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
namespace TALXIS.CLI.Features.Workspace;

[CliIdempotent]
[CliCommand(
    Description = "Scaffolds a component from a template and passes parameters",
    Name = "create")]
public class ComponentCreateCliCommand : TxcLeafCommand, ICliGetCompletions
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentCreateCliCommand));

    [CliArgument(Description = "Type of the component (e.g. 'pp-entity')")]
    public required string Type { get; set; }

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Directory path where the new component will be scaffolded", Required = true)]
    public string OutputPath { get; set; } = null!;

    // Conflicts with OutputPath in our templates
    // [CliOption(Name = "name", Aliases = ["-n"], Description = "The name for the created output. If not specified, the name of the output directory is used.", Required = false)]
    // public string? Name { get; set; }

    [CliOption(Description = "Component parameters which can be retrieved by parameter list command. Inputs need to be passed in the form key=value. Can be specified multiple times.")]
    public List<string> Param { get; set; } = new();

    protected override async Task<int> ExecuteAsync()
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
        var (success, failedActions) = await scaffolder.ScaffoldAsync(Type, OutputPath, parameters);
        if (success)
        {
            OutputFormatter.WriteResult("succeeded", $"Component scaffolded to {OutputPath} using template {Type}");
            return ExitSuccess;
        }
        else
        {
            Logger.LogError("Component scaffolded, but one or more post-actions failed. See errors above.");
            if (failedActions.Count > 0)
            {
                Logger.LogError("Summary of failed post-actions:");
                foreach (var failed in failedActions)
                {
                    Logger.LogError("- {FailedAction}", failed.Description ?? failed.ActionId.ToString());
                }
            }
            OutputFormatter.WriteResult("failed", "Component scaffolded, but one or more post-actions failed.");
            return ExitError;
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
#pragma warning disable RS0030 // Sync-over-async required: GetCompletions is a synchronous interface method
                    var templates = scaffolder.ListTemplatesAsync().Result;
#pragma warning restore RS0030
                    if (templates != null)
                    {
                        return templates.Select(t => new CompletionItem(t.ShortNameList.FirstOrDefault() ?? t.Name));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error fetching completions: {Message}", ex.Message);
                }
                break;
        }

        return Enumerable.Empty<CompletionItem>();
    }
}
