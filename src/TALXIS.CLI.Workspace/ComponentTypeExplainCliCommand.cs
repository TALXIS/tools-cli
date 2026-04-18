using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;
using TALXIS.CLI.Workspace.TemplateEngine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Explains a solution component type. Use names returned by 'component type list' command",
    Name = "explain"
)]
public class ComponentTypeExplainCliCommand
{
    private static readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ComponentTypeExplainCliCommand));

    [CliArgument(Description = "Type of the component to explain")]
    public required string Type { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            _logger.LogWarning("Please provide a component type");
            return 1;
        }

        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        var template = templates?.FirstOrDefault(t => string.Equals(t.Name, Type, StringComparison.OrdinalIgnoreCase)
            || t.ShortNameList.Any(sn => string.Equals(sn, Type, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            _logger.LogWarning("Component template {Type} not found", Type);
            return 1;
        }

        OutputWriter.WriteLine($"Type: {template.ShortNameList.FirstOrDefault()}");
        OutputWriter.WriteLine($"Description: {template.Description}");
        return 0;
    }
}
