using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// Removes a connection. Flows and connection references bound to it stop working.
/// Usage: <c>txc environment connection delete &lt;connection-id&gt; [--connector &lt;name&gt;] [--yes]</c>
/// </summary>
[CliDestructive("Permanently deletes the connection. Flows, apps and connection references bound to it stop working until they are bound to another connection.")]
[CliCommand(
    Name = "delete",
    Description = "Deletes a connection from the LIVE connected environment. Requires an active profile."
)]
public class ConnectionDeleteCliCommand : ConnectionCommandBase, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionService>();
        await service.DeleteAsync(Profile, Connector, ConnectionId, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = ConnectionOutcomeStatus.Deleted.ToKebabCase(), connectionId = ConnectionId },
            _ => OutputWriter.WriteLine($"Deleted connection '{ConnectionId}'."));

        return ExitSuccess;
    }
}
