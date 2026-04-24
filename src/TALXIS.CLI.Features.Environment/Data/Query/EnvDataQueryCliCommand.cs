using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Data.Query;

/// <summary>
/// Parent command for all query sub-commands (SQL, FetchXML, OData).
/// </summary>
[CliCommand(
    Name = "query",
    Description = "Execute data queries against the environment.",
    Children = new[] { typeof(EnvDataQuerySqlCliCommand), typeof(EnvDataQueryFetchXmlCliCommand), typeof(EnvDataQueryODataCliCommand) }
)]
public class EnvDataQueryCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
