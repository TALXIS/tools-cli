using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Configure and troubleshoot this tool"
)]
public class ConfigCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(
            Description = "Run checks to determine if this local machine is correctly set up, validate connections and settings",
            Name = "validate")]
    public class ConfigValidateCliCommand
    {
        public void Run(CliContext context)
        {
            Console.WriteLine(
      @"Hello"
            );
        }
    }
}
