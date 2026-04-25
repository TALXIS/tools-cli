using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Workspace.TemplateEngine;

namespace TALXIS.CLI.Features.Workspace;

[CliReadOnly]
[CliCommand(
    Description = "Explains a solution component type. Use names returned by 'component type list' command",
    Name = "explain"
)]
public class ComponentTypeExplainCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentTypeExplainCliCommand));

    [CliArgument(Description = "Type of the component to explain")]
    public required string Type { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            Logger.LogError("Please provide a component type");
            return ExitValidationError;
        }

        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        var template = templates?.FirstOrDefault(t => string.Equals(t.Name, Type, StringComparison.OrdinalIgnoreCase)
            || t.ShortNameList.Any(sn => string.Equals(sn, Type, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            Logger.LogError("Component template {Type} not found", Type);
            return ExitValidationError;
        }

        var data = new
        {
            type = template.ShortNameList.FirstOrDefault(),
            description = template.Description
        };

        OutputFormatter.WriteData(data, d =>
        {
            OutputWriter.WriteLine($"Type: {d.type}");
            OutputWriter.WriteLine($"Description: {d.description}");
        });

        return ExitSuccess;
    }
}
