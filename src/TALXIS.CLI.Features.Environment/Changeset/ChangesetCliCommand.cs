using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Changeset;

/// <summary>
/// <c>txc environment changeset</c> — manage staged operations for batch apply.
/// </summary>
[CliCommand(
    Name = "changeset",
    Description = "Manage staged operations for batch apply to the environment.",
    Children = new[] { typeof(ChangesetStatusCliCommand), typeof(ChangesetDiscardCliCommand), typeof(ChangesetApplyCliCommand) }
)]
public class ChangesetCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
