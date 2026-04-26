using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Data;

[CliCommand(
    Description = "Data-related utilities for ETL, Power Query and migration scenarios",
    ShortFormAutoGenerate = CliNameAutoGenerate.None
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
        Name = "server",
        ShortFormAutoGenerate = CliNameAutoGenerate.None)]
    public class TransformServerCliCommand
    {
        public void Run(CliContext context)
        {
            context.ShowHelp();
        }
    }
}
