using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Description = "Starts a local HTTP server exposing endpoints for ETL/data transformation tasks. Useful for integrating with Power Query or other local ETL tools."
)]
public class ServerCliCommand
{
    [CliOption(Description = "Optional. Port to run the server on (default: 50505)")]
    public int Port { get; set; } = 50505;

    public async Task<int> RunAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => {
            e.Cancel = true;
            cts.Cancel();
        };
        var server = new DataTransformationServer(Port);
        Console.WriteLine($"Press Ctrl+C to stop the server.");
        await server.StartAsync(cts.Token);
        return 0;
    }
}
