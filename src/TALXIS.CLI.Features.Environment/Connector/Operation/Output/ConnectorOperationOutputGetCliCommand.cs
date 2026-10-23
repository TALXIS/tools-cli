using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation.Output;

/// <summary>
/// <c>txc environment connector operation output get</c> — the fields a
/// dynamic output has (e.g. the columns of the row a Dataverse trigger
/// returns), each with the expression that reads it.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Gets the fields of one dynamic operation output, such as the columns of the row a Dataverse trigger or List rows returns, each with the exact expression that reads it in a later step. Pass the parameters it depends on with --value. Read through a bound connection reference: without --use-connection the answer only says whose connection would be used; ask the user, then re-run with --use-connection. Requires an active profile."
)]
public class ConnectorOperationOutputGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationOutputGetCliCommand));

    [CliArgument(Description = "Connector name, for example shared_commondataserviceforapps.")]
    public string Connector { get; set; } = null!;

    [CliArgument(Description = "Operation ID, for example CreateRecord.")]
    public string Operation { get; set; } = null!;

    [CliArgument(Description = "Output key, as 'connector operation get' lists it, for example body or body/value.")]
    public string Output { get; set; } = null!;

    [CliOption(Name = "--value", Description = "A value chosen for a parameter this output depends on, as name=value (e.g. entityName=contacts). Can be repeated.", Required = false)]
    public List<string> Value { get; set; } = new();

    [CliOption(Name = "--connection-reference", Description = "Connection reference to read dynamic values through. Optional when the connector has exactly one bound connection reference.", Required = false)]
    public string? ConnectionReference { get; set; }

    [CliOption(Name = "--use-connection", Description = "Read dynamic values through the connection. Pass only after the user has agreed to their connection being used.", Required = false)]
    public bool UseConnection { get; set; }

    [CliOption(Name = "--search", Description = "Show only values or fields whose name or label contains this text (e.g. account among 800 tables).", Required = false)]
    public string? Search { get; set; }

    protected override async Task<int> ExecuteAsync()
        => await DynamicValuesQuery.RunAsync(
            new DynamicValuesRequest(Profile, Connector, Operation, Output, IsOutput: true, Value, ConnectionReference, UseConnection, Search),
            Logger).ConfigureAwait(false)
            ? ExitSuccess
            : ExitValidationError;
}
