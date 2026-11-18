using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation.Parameter;

/// <summary>
/// <c>txc environment connector operation parameter get</c> — what a single
/// parameter can take: its values, or its sub-fields, in one call.
/// </summary>
/// <remarks>
/// Dynamic values come from the connector itself through a connection, which
/// acts as the person who owns it. Without <c>--use-connection</c> nothing is
/// called through it: the answer says whose connection would be used, so the
/// agent can ask first.
/// </remarks>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Gets what one connector operation parameter can take, in one call: its allowed values (e.g. table names, Teams locations) or its sub-fields (e.g. a Dataverse row's item/<column> keys, a Teams message's body/<field> keys). Pass the parameters it depends on with --value. Dynamic values are read through a bound connection reference: without --use-connection the answer only says whose connection would be used; ask the user, then re-run with --use-connection. Requires an active profile."
)]
public class ConnectorOperationParameterGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationParameterGetCliCommand));

    [CliArgument(Name = "connector", Description = "Connector name, for example shared_commondataserviceforapps.")]
    public string Connector { get; set; } = null!;

    [CliArgument(Name = "operation", Description = "Operation ID, for example CreateRecord.")]
    public string Operation { get; set; } = null!;

    [CliArgument(Name = "parameter", Description = "Parameter name, as 'connector operation get' lists it, for example item or subscriptionRequest/scope.")]
    public string Parameter { get; set; } = null!;

    [CliOption(Name = "--value", Description = "A value already chosen for a parameter this one depends on, as name=value (e.g. entityName=contacts). Can be repeated.", Required = false)]
    public List<string> Value { get; set; } = new();

    [CliOption(Name = "--connection-reference", Description = "Connection reference to read dynamic values through. Optional when the connector has exactly one bound connection reference.", Required = false)]
    public string? ConnectionReference { get; set; }

    [CliOption(Name = "--use-connection", Description = "Read dynamic values through the connection. Pass only after the user has agreed to their connection being used.", Required = false)]
    public bool UseConnection { get; set; }

    [CliOption(Name = "--filter", Description = "Show only values or fields whose name or label contains this text (e.g. account among 800 tables).", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
        => await DynamicValuesQuery.RunAsync(
            new DynamicValuesRequest(Profile, Connector, Operation, Parameter, IsOutput: false, Value, ConnectionReference, UseConnection, Filter),
            Logger).ConfigureAwait(false)
            ? ExitSuccess
            : ExitValidationError;
}
