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
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DocsGetCliCommand));

    [CliArgument(Description = "ID of the skill to show (e.g. 'component-creation', 'deployment-workflow')")]
    public string SkillId { get; set; } = "";

    protected override Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(SkillId))
        {
            Logger.LogError("Skill ID is required. Run `txc docs list` to see available skills.");
            return Task.FromResult(ExitValidationError);
        }

        var content = SkillContent.Load(SkillId);
        if (content is null)
        {
            Logger.LogError("Skill '{SkillId}' not found. Run `txc docs list` to see available skills.", SkillId);
            return Task.FromResult(ExitValidationError);
        }

        OutputFormatter.WriteValue("skill", content);
        return Task.FromResult(ExitSuccess);
    }
}
