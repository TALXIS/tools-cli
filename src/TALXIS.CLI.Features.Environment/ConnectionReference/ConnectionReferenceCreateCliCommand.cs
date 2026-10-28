using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.Connector;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// Creates a reference for a connector, optionally in a solution and already bound.
/// Usage: <c>txc environment connection-reference create &lt;logical-name&gt; --connector &lt;name&gt; --display-name &lt;name&gt; [--solution &lt;unique-name&gt;] [--connection-id &lt;id&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Creates a connection reference for a connector in the LIVE connected environment. Requires an active profile. Idempotent by logical name. A custom connector must already be deployed in the environment (import its solution first). Example: txc environment connection-reference create new_mail --connector shared_office365users --display-name \"Mail\" --solution MySolution --connection-id <connection-id>"
)]
public class ConnectionReferenceCreateCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionReferenceCreateCliCommand));

    [CliArgument(Name = "logical-name", Description = "Connection reference logical name, for example new_foodfacts. It is the stable key flows bind to.")]
    public string LogicalName { get; set; } = null!;

    [CliOption(Name = "--connector", Description = ConnectorDescriptions.Identifier, Required = true)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "Display name of the reference. Make it unique and descriptive.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--solution", Description = "Solution unique name to add the reference to. Without it the reference is not in any solution you own.", Required = false)]
    public string? Solution { get; set; }

    [CliOption(Name = "--connection-id", Description = "Connection to bind the reference to. You must own the connection or have it shared with you.", Required = false)]
    public string? ConnectionId { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionReferenceService>();
        var outcome = await service.CreateAsync(
            Profile,
            new ConnectionReferenceCreateOptions(LogicalName, DisplayName, Connector, ConnectionId, Solution),
            CancellationToken.None).ConfigureAwait(false);

        var result = ConnectionReferenceResult.From(outcome);
        OutputFormatter.WriteData(result, _ => OutputWriter.WriteLine(result.Describe()));
        return ExitSuccess;
    }
}
