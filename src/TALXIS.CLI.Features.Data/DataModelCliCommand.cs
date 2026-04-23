using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Data;

[CliCommand(
    Name = "model",
    Description = "Data modeling utilities",
    Children = new[] { typeof(DataModelConvertCliCommand) }
)]
public class DataModelCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
