using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation.Parameter;

/// <summary>States of a <see cref="ParameterValues"/> answer.</summary>
public static class ParameterValuesStatus
{
    /// <summary><see cref="ParameterValues.Values"/> lists what the parameter accepts.</summary>
    public const string Values = "values";
    /// <summary><see cref="ParameterValues.Fields"/> lists the sub-fields to fill (e.g. item/lastname).</summary>
    public const string Fields = "fields";
    /// <summary>Other parameters must be chosen first; see <see cref="ParameterValues.Missing"/>.</summary>
    public const string NeedsValues = "needs-values";
    /// <summary>The connection would be used: ask the user, then re-run with --use-connection.</summary>
    public const string NeedsConsent = "needs-consent";
    /// <summary>The values are not dynamic; they are listed from the operation spec.</summary>
    public const string Static = "static";
    public const string Unavailable = "unavailable";
}

/// <summary>The one answer to "what can I put in this parameter".</summary>
public sealed record ParameterValues(
    string Connector,
    string Operation,
    string Parameter,
    string Status,
    string Message,
    IReadOnlyList<AllowedValue>? Values,
    IReadOnlyList<ParameterView>? Fields,
    IReadOnlyList<string>? Missing,
    string? ConnectionReference,
    string? ConnectionOwner);

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

    [CliArgument(Description = "Connector name, for example shared_commondataserviceforapps.")]
    public string Connector { get; set; } = string.Empty;

    [CliArgument(Description = "Operation ID, for example CreateRecord.")]
    public string Operation { get; set; } = string.Empty;

    [CliArgument(Description = "Parameter name, as 'connector operation get' lists it, for example item or subscriptionRequest/scope.")]
    public string Parameter { get; set; } = string.Empty;

    [CliOption(Name = "--value", Description = "A value already chosen for a parameter this one depends on, as name=value (e.g. entityName=contacts). Can be repeated.", Required = false)]
    public List<string> Value { get; set; } = new();

    [CliOption(Name = "--connection-reference", Description = "Connection reference to read dynamic values through. Optional when the connector has exactly one bound connection reference.", Required = false)]
    public string? ConnectionReference { get; set; }

    [CliOption(Name = "--use-connection", Description = "Read dynamic values through the connection. Pass only after the user has agreed to their connection being used.", Required = false)]
    public bool UseConnection { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ConnectionReferenceRecord? reference = null;
        string? owner = null;
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

        var metadata = TxcServices.Get<IConnectorMetadataService>();
        var operation = await metadata.GetOperationAsync(Profile, Connector, Operation, CancellationToken.None).ConfigureAwait(false);
        var parameter = operation.Parameters.FirstOrDefault(p => p.Name == Parameter);
        if (parameter is null)
        {
            Logger.LogError(
                "'{Parameter}' is not a parameter of {Operation}. Its parameters: {Parameters}.",
                Parameter, Operation, string.Join(", ", operation.Parameters.Select(p => p.Name)));
            return ExitValidationError;
        }

        var view = ParameterViews.From(operation, parameter);
        if (view.ValuesFrom is not (ParameterSource.List or ParameterSource.Fields))
        {
            Write(Answer(view.ValuesFrom == ParameterSource.Picker ? ParameterValuesStatus.Unavailable : ParameterValuesStatus.Static,
                view.ValuesFrom == ParameterSource.Picker
                    ? "This is a folder or file picker; choose the value with the user."
                    : view.AllowedValues is { Count: > 0 } ? "Static values from the operation spec." : "Free value; nothing to look up.",
                values: view.AllowedValues));
            return ExitSuccess;
        }

        // Inputs the lookup needs are known from the spec alone: report them
        // before anything touches a connection.
        var missing = view.DependsOn?.Where(d => !values.ContainsKey(d)).ToList();
        if (missing is { Count: > 0 })
        {
            Write(Answer(ParameterValuesStatus.NeedsValues,
                $"Choose {string.Join(" and ", missing.Select(m => $"'{m}'"))} first and pass them with --value. Get their values with: " +
                string.Join("; ", missing.Select(m => ParameterViews.GetValuesCommand(Connector, Operation, m,
                    ParameterViews.From(operation, operation.Parameters.First(p => p.Name == m)).DependsOn))),
                missing: missing));
            return ExitSuccess;
        }

        (reference, var error) = await PickConnectionReferenceAsync().ConfigureAwait(false);
        if (reference is null)
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var resolution = await metadata.ResolveOperationAsync(
            Profile, Connector, Operation, reference.LogicalName, reference.ConnectionId!, values, Parameter, UseConnection,
            CancellationToken.None).ConfigureAwait(false);

        owner = resolution.Connection.OwnerName is { } name
            ? resolution.Connection.OwnerPrincipalName is { } upn ? $"{name} <{upn}>" : name
            : null;
        var lookup = resolution.Lookups.FirstOrDefault();

        ParameterValues answer;
        if (lookup is null || lookup.Status == DynamicLookupStatus.Skipped)
        {
            answer = Answer(lookup?.MissingValues is { Count: > 0 } ? ParameterValuesStatus.NeedsValues : ParameterValuesStatus.Unavailable,
                lookup?.Reason ?? "This parameter's values cannot be looked up.", missing: lookup?.MissingValues);
        }
        else if (!resolution.ConnectionUsed)
        {
            answer = Answer(ParameterValuesStatus.NeedsConsent,
                $"Reading these values uses connection reference '{reference.LogicalName}', owned by {owner ?? "an unknown user"}: " +
                "a read-only metadata lookup made as that person; nothing is created or changed. " +
                "Ask the user, and if they agree re-run with --use-connection.");
        }
        else if (lookup.Status != DynamicLookupStatus.Resolved)
        {
            answer = Answer(ParameterValuesStatus.Unavailable, $"The lookup failed: {lookup.Reason}");
        }
        else if (view.ValuesFrom == ParameterSource.Fields)
        {
            var fields = ParameterViews.From(resolution.Operation)
                .Where(f => f.Name.StartsWith(Parameter + "/", StringComparison.Ordinal))
                .ToList();
            answer = Answer(ParameterValuesStatus.Fields,
                $"{fields.Count} fields; use them as inputs.parameters keys exactly as named.", fields: fields);
        }
        else
        {
            var resolved = ParameterViews.From(resolution.Operation, resolution.Operation.Parameters.First(p => p.Name == Parameter));
            answer = Answer(ParameterValuesStatus.Values,
                $"{resolved.AllowedValues?.Count ?? 0} values; use the value, not the label.", values: resolved.AllowedValues);
        }

        Write(answer);
        return ExitSuccess;

        ParameterValues Answer(string status, string message,
            IReadOnlyList<AllowedValue>? values = null, IReadOnlyList<ParameterView>? fields = null, IReadOnlyList<string>? missing = null)
            => new(Connector, Operation, Parameter, status, message, values, fields, missing, reference?.LogicalName, owner);
    }

    private async Task<(ConnectionReferenceRecord? Reference, string? Error)> PickConnectionReferenceAsync()
    {
        var references = await TxcServices.Get<IConnectionReferenceService>()
            .ListAsync(Profile, Connector, CancellationToken.None).ConfigureAwait(false);
        var bound = references.Where(r => !string.IsNullOrWhiteSpace(r.ConnectionId)).ToList();

        if (!string.IsNullOrWhiteSpace(ConnectionReference))
        {
            var named = references.FirstOrDefault(r => string.Equals(r.LogicalName, ConnectionReference, StringComparison.OrdinalIgnoreCase));
            if (named is null)
                return (null, $"Connection reference '{ConnectionReference}' for '{Connector}' does not exist. Available: {(references.Count == 0 ? "none" : string.Join(", ", references.Select(r => r.LogicalName)))}.");
            if (string.IsNullOrWhiteSpace(named.ConnectionId))
                return (null, $"Connection reference '{named.LogicalName}' is not bound to a connection. Ask the user to bind one.");
            return (named, null);
        }

        return bound.Count switch
        {
            1 => (bound[0], null),
            0 => (null, $"No connection reference for '{Connector}' is bound to a connection. Ask the user to create one (in a solution: New > More > Connection reference) and run this again."),
            _ => (null, $"'{Connector}' has several bound connection references ({string.Join(", ", bound.Select(r => r.LogicalName))}); ask the user which one to use and pass --connection-reference."),
        };
    }

    private static void Write(ParameterValues answer) => OutputFormatter.WriteData(answer, Print);

#pragma warning disable TXC003
    private static void Print(ParameterValues answer)
    {
        OutputWriter.WriteLine($"{answer.Operation} {answer.Parameter}: {answer.Status}");
        OutputWriter.WriteLine(answer.Message);

        if (answer.Values is { Count: > 0 } values)
        {
            OutputWriter.WriteLine(string.Empty);
            var width = Math.Max(values.Max(v => v.Value.Length), 5);
            foreach (var v in values)
                OutputWriter.WriteLine(v.Label is null || v.Label == v.Value ? v.Value : $"{v.Value.PadRight(width)} | {v.Label}");
        }

        if (answer.Fields is { Count: > 0 } fields)
        {
            OutputWriter.WriteLine(string.Empty);
            OperationTextRenderer.PrintParameters(fields);
        }
    }
#pragma warning restore TXC003
}
