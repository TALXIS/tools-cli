

using System.CommandLine;
using System.CommandLine.Invocation;
using TALXIS.CLI.Data;

class Program
{
    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand("TALXIS CLI - txc");
        rootCommand.Subcommands.Add(DataCommand.CreateCommand());
        var parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }
}
