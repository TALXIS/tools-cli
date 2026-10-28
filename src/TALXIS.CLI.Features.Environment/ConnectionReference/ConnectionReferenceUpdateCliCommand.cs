using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// Binds the reference to a connection. This is how a deployment points a solution at an
/// environment's own connection.
/// Usage: <c>txc environment connection-reference update &lt;logical-name&gt; [--connection-id &lt;id&gt;] [--display-name &lt;name&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Binds a connection reference to a connection, or renames it, in the LIVE connected environment. Requires an active profile. Idempotent. Binding fails unless you own the connection or it is shared with you (see 'txc environment connection permission add'). A binding cannot be cleared. Example: txc environment connection-reference update new_mail --connection-id <connection-id>"
)]
public class ConnectionReferenceUpdateCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionReferenceUpdateCliCommand));

    [CliArgument(Name = "logical-name", Description = "Connection reference logical name.")]
    public string LogicalName { get; set; } = null!;

    [CliOption(Name = "--connection-id", Description = "Connection to bind the reference to.", Required = false)]
    public string? ConnectionId { get; set; }

    [CliOption(Name = "--display-name", Description = "New display name.", Required = false)]
    public string? DisplayName { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionReferenceService>();
        var outcome = await service.UpdateAsync(Profile, LogicalName, ConnectionId, DisplayName, CancellationToken.None).ConfigureAwait(false);

        var result = ConnectionReferenceResult.From(outcome);
        OutputFormatter.WriteData(result, _ => OutputWriter.WriteLine(result.Describe()));
        return ExitSuccess;
    }
}
