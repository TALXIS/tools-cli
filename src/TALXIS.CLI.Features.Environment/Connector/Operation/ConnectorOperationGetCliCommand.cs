using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Gets one connector operation from the LIVE connected environment: exact inputs.parameters keys, types, allowed values and action type, plus a ready-to-paste snippet for the flow definition (connectionReferences entry and the trigger or action). Requires an active profile. Use the snippet instead of writing connector JSON by hand; if parameters are dynamic, use 'connector operation resolve' instead."
)]
public class ConnectorOperationGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationGetCliCommand));

    [CliArgument(Description = "Connector name, for example shared_commondataserviceforapps.")]
    public string Connector { get; set; } = string.Empty;

    [CliArgument(Description = "Operation ID, for example CreateRecord.")]
    public string Operation { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorMetadataService>();
        var operation = await service.GetOperationAsync(Profile, Connector, Operation, CancellationToken.None)
            .ConfigureAwait(false);

        var result = new OperationSpec(operation, FlowSnippet.Build(operation));
        OutputFormatter.WriteData(result, PrintSpec);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData.
    private static void PrintSpec(OperationSpec spec)
    {
        OperationTextRenderer.Print(spec.Operation);
        OperationTextRenderer.PrintSnippet(spec.Snippet);
    }
}

/// <summary>An operation's spec plus the JSON to paste into the flow definition.</summary>
public sealed record OperationSpec(OperationDetail Operation, System.Text.Json.Nodes.JsonObject Snippet);
