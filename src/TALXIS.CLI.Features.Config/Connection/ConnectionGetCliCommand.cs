using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection show &lt;name&gt;</c> — emit a single
/// connection as JSON. Exits with code 2 when the connection is not
/// found (so scripts can distinguish "missing" from "error").
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a single connection as JSON. Exit 2 if not found."
)]
public class ConnectionGetCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionGetCliCommand));

    [CliArgument(Description = "Connection name.")]
    public required string Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("Connection name must be provided.");
            return ExitError;
        }

        var store = TxcServices.Get<IConnectionStore>();
        var connection = await store.GetAsync(Name, CancellationToken.None).ConfigureAwait(false);
        if (connection is null)
        {
            Logger.LogError("Connection '{Name}' not found.", Name);
            return ExitValidationError;
        }

        OutputFormatter.WriteData(connection);
        return ExitSuccess;
    }
}
