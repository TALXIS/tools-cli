using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Features.Workspace.TemplateEngine;

namespace TALXIS.CLI.Features.Workspace;

[CliReadOnly]
[CliCommand(
    Description = "Lists component types available for LOCAL scaffolding via 'workspace component create'. Reads from built-in templates, not from a live environment.",
    Name = "list"
)]
public class ComponentTypeListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentTypeListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        using var scaffolder = new TemplateInvoker();
        var templates = await scaffolder.ListTemplatesAsync();
        if (templates == null || !templates.Any())
        {
            OutputFormatter.WriteList(Array.Empty<object>().ToList().AsReadOnly(), items =>
            {
                OutputWriter.WriteLine("No components available.");
            });
            return ExitSuccess;
        }

        var projected = templates.Select(t => new
        {
            shortName = t.ShortNameList.FirstOrDefault(),
            description = t.Description
        }).ToList();

        OutputFormatter.WriteList(projected, items =>
        {
            foreach (var item in items)
            {
                var desc = string.IsNullOrWhiteSpace(item.description) ? "" : $" - {item.description}";
                OutputWriter.WriteLine($"- {item.shortName}{desc}");
            }
        });

        return ExitSuccess;
    }
}
