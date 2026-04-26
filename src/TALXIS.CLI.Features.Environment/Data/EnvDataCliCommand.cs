using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Data;

/// <summary>
/// Parent command that groups all data operations against a live Dataverse
/// environment — queries, single-record CRUD, and bulk operations.
/// </summary>
[CliCommand(
    Name = "data",
    Description = "Data operations against a live Dataverse environment (query, record CRUD, bulk operations)",
    Children = new[] { typeof(Query.EnvDataQueryCliCommand), typeof(Record.EnvDataRecordCliCommand), typeof(Bulk.EnvDataBulkCliCommand) }
)]
public class EnvDataCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
