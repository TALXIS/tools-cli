using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

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
