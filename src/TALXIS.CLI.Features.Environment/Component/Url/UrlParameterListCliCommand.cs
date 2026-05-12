using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Lists the URL parameters accepted by a given component type.
/// Does not require a profile — reads from the static <see cref="UrlParameterRegistry"/>.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List URL parameters accepted by a component type.",
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UrlParameterListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<UrlParameterListCliCommand>();

    [CliOption(Name = "--type", Description = "Component type to list URL parameters for.", Required = true)]
    public string Type { get; set; } = null!;

    protected override Task<int> ExecuteAsync()
    {
        // Resolve the canonical type name via the registry
        var def = ComponentDefinitionRegistry.GetByName(Type);
        var typeName = def?.Name ?? Type;

        var parameters = UrlParameterRegistry.GetParameters(typeName);

#pragma warning disable TXC003 // OutputWriter used inside OutputFormatter textRenderer callbacks
        OutputFormatter.WriteList(
            parameters.ToList(),
            items =>
            {
                OutputWriter.WriteLine($"URL parameters for type '{typeName}':");
                OutputWriter.WriteLine();
                foreach (var p in items)
                {
                    var required = p.Required ? " (required)" : "";
                    var defaultVal = p.DefaultValue != null ? $" [default: {p.DefaultValue}]" : "";
                    OutputWriter.WriteLine($"  {p.Name,-20} {p.Description}{required}{defaultVal}");
                }

                if (!UrlParameterRegistry.GetRegisteredTypes().Contains(typeName, StringComparer.OrdinalIgnoreCase))
                {
                    OutputWriter.WriteLine();
                    OutputWriter.WriteLine($"  Note: '{typeName}' does not have type-specific parameters. Showing common parameters.");
                }
            });
#pragma warning restore TXC003

        return Task.FromResult(ExitSuccess);
    }
}
