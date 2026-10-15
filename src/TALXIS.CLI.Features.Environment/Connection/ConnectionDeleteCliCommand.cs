using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection delete</c> — removes a connection. Flows and
/// connection references bound to it stop working.
/// </summary>
[CliDestructive("Permanently deletes the connection. Flows, apps and connection references bound to it stop working until they are bound to another connection.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a connection from the LIVE connected environment. Requires an active profile."
)]
public class ConnectionDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionDeleteCliCommand));

    [CliArgument(Name = "connection-id", Description = "Connection id, exactly as shown by 'environment connection list'.")]
    public string ConnectionId { get; set; } = null!;

    [CliOption(Name = "--connector", Description = "Connector of the connection (logical name, apiId or exact display name).", Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionService>();
        await service.DeleteAsync(Profile, Connector, ConnectionId, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = "deleted", connectionId = ConnectionId },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Deleted connection '{ConnectionId}'.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
