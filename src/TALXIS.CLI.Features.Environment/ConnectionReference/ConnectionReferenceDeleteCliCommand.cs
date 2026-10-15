using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// <c>txc environment connection-reference delete</c> — removes a reference.
/// </summary>
[CliDestructive("Permanently deletes the connection reference. Flows and apps that bind to it can no longer find their connection.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a connection reference from the LIVE connected environment. Requires an active profile."
)]
public class ConnectionReferenceDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionReferenceDeleteCliCommand));

    [CliArgument(Name = "logical-name", Description = "Connection reference logical name.")]
    public string LogicalName { get; set; } = null!;

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionReferenceService>();
        await service.DeleteAsync(Profile, LogicalName, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new { status = "deleted", logicalName = LogicalName },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Deleted connection reference '{LogicalName}'.");
#pragma warning restore TXC003
            });

        return ExitSuccess;
    }
}
