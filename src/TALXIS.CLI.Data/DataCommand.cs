
using System.CommandLine;


namespace TALXIS.CLI.Data;

public static class DataCommand
{
    public static Command CreateCommand()
    {
        var dataCommand = new Command("data", "Data-related commands.");

        var textArg = new Argument<string>("text")
        {
            Description = "Text to echo back."
        };
        var echoCommand = new Command("echo", "Echoes the input text.");
        echoCommand.Arguments.Add(textArg);
        echoCommand.SetAction(parseResult =>
        {
            var text = parseResult.GetValue(textArg);
            Console.WriteLine(text);
            return 0;
        });
        dataCommand.Subcommands.Add(echoCommand);
        return dataCommand;
    }
}


