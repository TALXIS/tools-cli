using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection show &lt;name&gt;</c> — emit a single
/// connection as JSON. Exits with code 2 when the connection is not
/// found (so scripts can distinguish "missing" from "error").
/// </summary>
[CliCommand(
    Name = "show",
    Description = "Show a single connection as JSON. Exit 2 if not found."
)]
public class ConnectionShowCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ConnectionShowCliCommand));

    [CliArgument(Description = "Connection name.")]
    public required string Name { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogError("Connection name must be provided.");
            return 1;
        }

        try
        {
            var store = TxcServices.Get<IConnectionStore>();
            var connection = await store.GetAsync(Name, CancellationToken.None).ConfigureAwait(false);
            if (connection is null)
            {
                _logger.LogError("Connection '{Name}' not found.", Name);
                return 2;
            }

            OutputWriter.WriteLine(JsonSerializer.Serialize(connection, TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show connection '{Name}'.", Name);
            return 1;
        }
    }
}
