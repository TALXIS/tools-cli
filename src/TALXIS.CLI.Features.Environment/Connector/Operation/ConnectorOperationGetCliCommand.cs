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
    Description = "Answers which parameters an operation needs, from the LIVE connected environment: every inputs.parameters key with type, requiredness and allowed values; for dynamic parameters what they depend on and the exact 'connector operation parameter get' command that fetches their values; plus the action type and a JSON skeleton for the flow definition. Requires an active profile."
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

        OutputFormatter.WriteData(OperationSpec.From(operation), PrintSpec);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData.
    private static void PrintSpec(OperationSpec spec)
    {
        OperationTextRenderer.Print(spec);
        OperationTextRenderer.PrintSnippet(spec.Snippet);
    }
}

/// <summary>
/// The answer to "which parameters do I need to input": every parameter with
/// its allowed values, and for dynamic ones what they depend on and the one
/// command that fetches them; plus the JSON skeleton for the flow definition.
/// </summary>
public sealed record OperationSpec(
    string Connector,
    string ApiId,
    string OperationId,
    string? Summary,
    bool IsTrigger,
    string ActionType,
    bool IsDeprecated,
    IReadOnlyList<ParameterView> Parameters,
    System.Text.Json.Nodes.JsonObject Snippet)
{
    public static OperationSpec From(OperationDetail operation)
        => new(operation.Connector, operation.ApiId, operation.OperationId, operation.Summary, operation.IsTrigger,
            operation.ActionType, operation.IsDeprecated, ParameterViews.From(operation), FlowSnippet.Build(operation));
}
