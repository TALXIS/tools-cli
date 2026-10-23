using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// <c>txc environment connector operation resolve</c> — resolves an operation's
/// dynamic parameters through a connection, the way the flow designer does.
/// </summary>
/// <remarks>
/// Using a connection acts as the person who owns it, so this is a two-step
/// command: without <c>--use-connection</c> it only reports the plan (whose
/// connection, which lookups), which the agent shows the user before asking
/// to run it.
/// </remarks>
[CliReadOnly]
[CliCommand(
    Name = "resolve",
    Description = "Resolves an operation's dynamic parameters (value lists and sub-schemas such as a Dataverse row or a Teams message body) through a connection in the LIVE environment, as the flow designer does, and returns a ready-to-paste snippet. Without --use-connection it calls nothing and returns the plan: whose connection would be used and which read-only lookups would be made. Show that plan to the user and re-run with --use-connection only after they agree. Pass every choice up front with --value. Requires an active profile."
)]
public class ConnectorOperationResolveCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationResolveCliCommand));

    [CliArgument(Description = "Connector name, for example shared_teams.")]
    public string Connector { get; set; } = string.Empty;

    [CliArgument(Description = "Operation ID, for example PostMessageToConversation.")]
    public string Operation { get; set; } = string.Empty;

    [CliOption(Name = "--connection-reference", Description = "Logical name of the connection reference whose connection the lookups go through. Optional when the connector has exactly one bound connection reference.", Required = false)]
    public string? ConnectionReference { get; set; }

    [CliOption(Name = "--value", Description = "A value chosen for another parameter that lookups take as input, as name=value, e.g. poster=Flow bot. Can be repeated.", Required = false)]
    public List<string> Value { get; set; } = new();

    [CliOption(Name = "--use-connection", Description = "Actually make the planned lookups through the connection. Pass only after the user has agreed to the plan.", Required = false)]
    public bool UseConnection { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Value)
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                Logger.LogError("--value '{Value}' must have the form name=value.", pair);
                return ExitValidationError;
            }

            values[pair[..separator]] = pair[(separator + 1)..];
        }

        var references = await TxcServices.Get<IConnectionReferenceService>()
            .ListAsync(Profile, Connector, CancellationToken.None).ConfigureAwait(false);
        var bound = references.Where(r => !string.IsNullOrWhiteSpace(r.ConnectionId)).ToList();

        ConnectionReferenceRecord? reference;
        if (!string.IsNullOrWhiteSpace(ConnectionReference))
        {
            reference = references.FirstOrDefault(r =>
                string.Equals(r.LogicalName, ConnectionReference, StringComparison.OrdinalIgnoreCase));
            if (reference is null)
            {
                Logger.LogError(
                    "Connection reference '{Reference}' for connector '{Connector}' does not exist. Available: {Available}.",
                    ConnectionReference, Connector,
                    references.Count == 0 ? "none" : string.Join(", ", references.Select(r => r.LogicalName)));
                return ExitValidationError;
            }
        }
        else if (bound.Count == 1)
        {
            reference = bound[0];
        }
        else
        {
            Logger.LogError(
                bound.Count == 0
                    ? "No connection reference for connector '{Connector}' is bound to a connection. Ask the user to create one (in a solution: New > More > Connection reference) and run this again."
                    : "Connector '{Connector}' has several bound connection references ({Available}); ask the user which one to use and pass --connection-reference.",
                Connector, string.Join(", ", bound.Select(r => r.LogicalName)));
            return ExitValidationError;
        }

        if (string.IsNullOrWhiteSpace(reference.ConnectionId))
        {
            Logger.LogError(
                "Connection reference '{Reference}' is not bound to a connection. Ask the user to bind one before resolving through it.",
                reference.LogicalName);
            return ExitValidationError;
        }

        var resolution = await TxcServices.Get<IConnectorMetadataService>().ResolveOperationAsync(
            Profile, Connector, Operation, reference.LogicalName, reference.ConnectionId, values, UseConnection,
            CancellationToken.None).ConfigureAwait(false);

        var result = new ResolveResult(
            resolution.ConnectionReference, resolution.Connection, resolution.ConnectionUsed, resolution.Lookups,
            resolution.Operation, FlowSnippet.Build(resolution.Operation, resolution.ConnectionReference, values),
            FlowSnippet.CheckValues(resolution.Operation, values));
        OutputFormatter.WriteData(result, PrintResolution);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintResolution(ResolveResult resolution)
    {
        var c = resolution.Connection;
        var owner = c.OwnerName is null ? "unknown owner" : $"{c.OwnerName}{(c.OwnerPrincipalName is null ? "" : $" <{c.OwnerPrincipalName}>")}";
        OutputWriter.WriteLine($"Connection: {resolution.ConnectionReference} -> {c.ConnectionId} (owned by {owner}, {c.Status ?? "status unknown"})");
        OutputWriter.WriteLine(string.Empty);

        if (!resolution.ConnectionUsed)
        {
            OutputWriter.WriteLine("PLAN ONLY - nothing has been called through this connection.");
            OutputWriter.WriteLine("These read-only lookups would be made as the connection's owner:");
        }

        foreach (var l in resolution.Lookups)
        {
            var arguments = l.Arguments.Count == 0 ? string.Empty : " (" + string.Join(", ", l.Arguments.Select(a => $"{a.Key}={a.Value}")) + ")";
            OutputWriter.WriteLine($"  [{l.Status}] {l.Parameter}: {l.Method} {l.OperationId}{arguments}");
            if (l.Reason is not null)
                OutputWriter.WriteLine($"           {l.Reason}");
        }

        if (resolution.Lookups.Count == 0)
            OutputWriter.WriteLine("  (this operation has no dynamic parameters)");

        if (!resolution.ConnectionUsed)
        {
            OutputWriter.WriteLine(string.Empty);
            OutputWriter.WriteLine("Re-run with --use-connection once the user agrees.");
            return;
        }

        OutputWriter.WriteLine(string.Empty);
        OperationTextRenderer.Print(resolution.Operation);
        foreach (var warning in resolution.Warnings)
            OutputWriter.WriteLine($"WARNING: {warning}");
        OperationTextRenderer.PrintSnippet(resolution.Snippet);
    }
#pragma warning restore TXC003
}

/// <summary>A resolution plus the JSON to paste into the flow definition.</summary>
public sealed record ResolveResult(
    string ConnectionReference,
    ConnectionDetail Connection,
    bool ConnectionUsed,
    IReadOnlyList<DynamicLookup> Lookups,
    OperationDetail Operation,
    System.Text.Json.Nodes.JsonObject Snippet,
    IReadOnlyList<string> Warnings);
