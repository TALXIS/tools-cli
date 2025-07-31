
using System.CommandLine;


namespace TALXIS.CLI.Data;

public static class DataCommand
{
    public static Command CreateCommand()
    {
        var dataCommand = new Command("data", "Data-related commands.");

        dataCommand.Subcommands.Add(ServerCommand.Create());
        return dataCommand;
    }
}


