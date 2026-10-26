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
    Description = "Answers which parameters an operation needs and what it returns, from the LIVE connected environment. Lists every inputs.parameters key with type, requiredness and allowed values, and every output with the expression that reads it. For dynamic parameters and outputs it gives the exact txc command that fetches their values or fields. Also gives the action type and a JSON skeleton. Requires an active profile."
)]
public class ConnectorOperationGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationGetCliCommand));

    [CliArgument(Name = "connector", Description = "Connector name, for example shared_commondataserviceforapps.")]
    public string Connector { get; set; } = null!;

    [CliArgument(Name = "operation", Description = "Operation ID, for example CreateRecord.")]
    public string Operation { get; set; } = null!;

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
    string? Description,
    bool IsTrigger,
    string ActionType,
    bool IsDeprecated,
    IReadOnlyList<ParameterView> Parameters,
    IReadOnlyList<OutputView> Outputs,
    System.Text.Json.Nodes.JsonObject Snippet)
{
    public static OperationSpec From(OperationDetail operation)
        => new(operation.Connector, operation.ApiId, operation.OperationId, operation.Summary, operation.Description, operation.IsTrigger,
            operation.ActionType, operation.IsDeprecated, ParameterViews.From(operation), OutputViews.From(operation),
            FlowSnippet.Build(operation));
}
