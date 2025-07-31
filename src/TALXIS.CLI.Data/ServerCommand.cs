using System.CommandLine;

namespace TALXIS.CLI.Data;

public static class ServerCommand
{
    public static Command Create()
    {
        var serverCommand = new Command("server", "Starts a local ETL HTTP server for data transformations.");
        var portOption = new Option<int>("--port")
        {
            Description = "Optional. Port to run the server on (default: 50505)",
            Arity = ArgumentArity.ZeroOrOne
        };
        portOption.DefaultValueFactory = _ => 50505;
        serverCommand.Options.Add(portOption);
        serverCommand.SetAction((Func<ParseResult, Task<int>>)(async parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                cts.Cancel();
            };
            var server = new DataTransformationServer(port);
            Console.WriteLine($"Press Ctrl+C to stop the server.");
            await server.StartAsync(cts.Token);
            return 0;
        }));
        return serverCommand;
    }
}
