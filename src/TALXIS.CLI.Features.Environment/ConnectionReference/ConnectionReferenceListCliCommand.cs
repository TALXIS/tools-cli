using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// Lists references with their binding status; <c>--unbound</c> is the deployment gate.
/// Usage: <c>txc environment connection-reference list [--solution &lt;unique-name&gt;] [--connector &lt;name&gt;] [--connection-id &lt;id&gt;] [--unbound] [--filter &lt;text&gt;]</c>
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists connection references in the LIVE connected environment with their binding status (bound, unbound, broken or unknown). Requires an active profile. Use --unbound after an import to find references that still need a connection."
)]
public class ConnectionReferenceListCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionReferenceListCliCommand));

    [CliOption(Name = "--solution", Description = "Show only references in this solution (unique name).", Required = false)]
    public string? Solution { get; set; }

    [CliOption(Name = "--connector", Description = "Show only references for this connector (logical name, apiId or exact display name).", Required = false)]
    public string? Connector { get; set; }

    [CliOption(Name = "--connection-id", Description = "Show only references bound to this connection.", Required = false)]
    public string? ConnectionId { get; set; }

    [CliOption(Name = "--unbound", Description = "Show only references that have no connection.", Required = false)]
    public bool Unbound { get; set; }

    [CliOption(Name = "--filter", Description = "Show only references whose logical name or display name contains this text.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionReferenceService>();
        var references = await service.ListAsync(
            Profile,
            new ConnectionReferenceListOptions(Solution, Connector, ConnectionId, Unbound, Filter),
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(references, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectionReferenceRecord> references)
    {
        if (references.Count == 0)
        {
            OutputWriter.WriteLine("No connection references found.");
            return;
        }

        var header = $"{"Logical name".PadRight(36)} | {"Connector".PadRight(30)} | {"Binding".PadRight(8)} | {"Connection owner".PadRight(30)} | Connection id";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var r in references)
        {
            OutputWriter.WriteLine(
                $"{TableText.Truncate(r.LogicalName, 36).PadRight(36)} | {TableText.Truncate(r.ApiId, 30).PadRight(30)} | {r.BindingStatus.PadRight(8)} | " +
                $"{TableText.Truncate(r.ConnectionOwner ?? "-", 30).PadRight(30)} | {r.ConnectionId}");
        }
    }

#pragma warning restore TXC003
}
