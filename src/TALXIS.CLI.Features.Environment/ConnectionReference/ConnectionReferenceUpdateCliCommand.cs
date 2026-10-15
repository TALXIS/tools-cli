using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// <c>txc environment connection-reference update</c> — binds the reference to
/// a connection. This is how a deployment points a solution at an
/// environment's own connection.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Bind a connection reference to a connection, or rename it, in the LIVE connected environment. Requires an active profile. Idempotent. Binding fails with ConnectionAuthorizationFailed unless you own the connection or it is shared with you (see 'environment connection permission add'). A binding cannot be cleared."
)]
public class ConnectionReferenceUpdateCliCommand : ProfiledCliCommand
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

        ConnectionReferenceOutput.Write(outcome);
        return ExitSuccess;
    }
}
