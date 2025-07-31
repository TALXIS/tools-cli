using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Description = "Starts a local HTTP server exposing endpoints for ETL/data transformation tasks. Useful for integrating with Power Query or other local ETL tools. " +
    "\n\nAvailable endpoints:" +
    "\n  POST /ComputePrimaryKey — Accepts a JSON body with 'entity' and 'alternateKeys' to return a deterministic GUID primary key. " +
    "\n    The 'alternateKeys' are a set of attributes that together uniquely identify a record and never change for its lifetime. "
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
