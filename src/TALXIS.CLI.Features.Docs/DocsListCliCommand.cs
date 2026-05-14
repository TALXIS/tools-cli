using System.Reflection;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Docs;

[CliCommand(
    Name = "list",
    Description = "Lists available skills in the knowledge base."
)]
[CliReadOnly]
public class DocsListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<DocsListCliCommand>();

    protected override Task<int> ExecuteAsync()
    {
        var assembly = typeof(DocsListCliCommand).Assembly;
        var indexResource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Skills.index.json"));

        if (indexResource is null)
        {
            OutputFormatter.WriteValue("skills", "No skills found.");
            return Task.FromResult(ExitSuccess);
        }

        using var stream = assembly.GetManifestResourceStream(indexResource);
        if (stream is null)
        {
            OutputFormatter.WriteValue("skills", "No skills found.");
            return Task.FromResult(ExitSuccess);
        }

        var entries = JsonSerializer.Deserialize<List<SkillIndexEntry>>(stream, TxcJsonOptions.Default);

        if (entries is null || entries.Count == 0)
        {
            OutputFormatter.WriteValue("skills", "No skills found.");
            return Task.FromResult(ExitSuccess);
        }

        OutputFormatter.WriteList(entries.Cast<object>().ToList().AsReadOnly(), items =>
        {
            foreach (var item in items.Cast<SkillIndexEntry>())
            {
                OutputFormatter.WriteValue("skill", $"{item.Id,-25} {item.Summary}");
            }
        });

        return Task.FromResult(ExitSuccess);
    }

    private class SkillIndexEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
    }
}
