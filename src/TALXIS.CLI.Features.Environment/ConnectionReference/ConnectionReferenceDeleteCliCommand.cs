using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// Removes a reference.
/// Usage: <c>txc environment connection-reference delete &lt;logical-name&gt; [--yes]</c>
/// </summary>
[CliDestructive("Permanently deletes the connection reference. Flows and apps that bind to it can no longer find their connection.")]
[CliCommand(
    Name = "delete",
    Description = "Deletes a connection reference from the LIVE connected environment. Requires an active profile."
)]
public class ConnectionReferenceDeleteCliCommand : ConnectivityCommandBase, IDestructiveCommand
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
            new { status = ConnectionOutcomeStatus.Deleted.ToKebabCase(), logicalName = LogicalName },
            _ => OutputWriter.WriteLine($"Deleted connection reference '{LogicalName}'."));

        return ExitSuccess;
    }
}
