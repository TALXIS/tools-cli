using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Parent command for bulk operations (CreateMultiple, UpdateMultiple,
/// UpsertMultiple) against a live Dataverse environment.
/// </summary>
[CliCommand(
    Name = "bulk",
    Description = "Bulk operations (CreateMultiple, UpdateMultiple, UpsertMultiple) against the environment.",
    Children = new[] { typeof(EnvDataBulkCreateCliCommand), typeof(EnvDataBulkUpdateCliCommand), typeof(EnvDataBulkUpsertCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EnvDataBulkCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
