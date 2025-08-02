using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Description = "Data-related utilities for ETL, Power Query and migration scenarios"
)]
public class TransformCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
    [CliCommand(
        Description = "HTTP server for ETL and data transformation tasks",
        Children = new[] { typeof(TransformServerStartCliCommand) },
        Name = "server")]
    public class TransformServerCliCommand
    {
        public void Run(CliContext context)
        {
            context.ShowHelp();
        }
    }
}
