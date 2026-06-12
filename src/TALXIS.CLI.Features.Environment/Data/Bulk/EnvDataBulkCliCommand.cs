using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Data.Bulk;

/// <summary>
/// Parent command for bulk operations (CreateMultiple, UpdateMultiple,
/// UpsertMultiple, DeleteMultiple) against a live Dataverse environment.
/// </summary>
[CliCommand(
    Name = "bulk",
    Description = "Bulk operations (CreateMultiple, UpdateMultiple, UpsertMultiple, DeleteMultiple) against the environment.",
    Children = new[] { typeof(EnvDataBulkCreateCliCommand), typeof(EnvDataBulkUpdateCliCommand), typeof(EnvDataBulkUpsertCliCommand), typeof(EnvDataBulkDeleteCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EnvDataBulkCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
