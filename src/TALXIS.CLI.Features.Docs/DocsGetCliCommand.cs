using System.Reflection;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Docs;

[CliCommand(
    Name = "get",
    Description = "Get the full content of a skill from the knowledge base."
)]
[CliReadOnly]
public class DocsGetCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<DocsGetCliCommand>();

    [CliArgument(Description = "ID of the skill to show (e.g. 'component-creation', 'deployment-workflow')")]
    public string SkillId { get; set; } = "";

    protected override Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(SkillId))
        {
            Logger.LogError("Skill ID is required. Run `txc docs list` to see available skills.");
            return Task.FromResult(ExitValidationError);
        }

        var assembly = typeof(DocsGetCliCommand).Assembly;

        // Try exact match, then with underscores for hyphens
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Skills.{SkillId}.md") ||
                                 n.EndsWith($"Skills.{SkillId.Replace("-", "_")}.md"));

        if (resourceName is null)
        {
            Logger.LogError("Skill '{SkillId}' not found. Run `txc docs list` to see available skills.", SkillId);
            return Task.FromResult(ExitValidationError);
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            Logger.LogError("Could not load skill '{SkillId}'.", SkillId);
            return Task.FromResult(ExitError);
        }

        using var reader = new StreamReader(stream);
        OutputFormatter.WriteValue("skill", reader.ReadToEnd());
        return Task.FromResult(ExitSuccess);
    }
}
