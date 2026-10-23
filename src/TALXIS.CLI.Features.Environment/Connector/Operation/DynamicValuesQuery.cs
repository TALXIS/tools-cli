using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>States of a <see cref="DynamicValuesAnswer"/>.</summary>
public static class DynamicValuesStatus
{
    /// <summary><see cref="DynamicValuesAnswer.Values"/> lists what the parameter accepts.</summary>
    public const string Values = "values";
    /// <summary>The sub-fields: <see cref="DynamicValuesAnswer.Fields"/> for a parameter, <see cref="DynamicValuesAnswer.Outputs"/> for an output.</summary>
    public const string Fields = "fields";
    /// <summary>Other parameters must be chosen first; see <see cref="DynamicValuesAnswer.Missing"/>.</summary>
    public const string NeedsValues = "needs-values";
    /// <summary>The connection would be used: ask the user, then re-run with --use-connection.</summary>
    public const string NeedsConsent = "needs-consent";
    /// <summary>Nothing to look up; any allowed values come from the operation spec.</summary>
    public const string Static = "static";
    public const string Unavailable = "unavailable";
}

/// <summary>The one answer to "what can this parameter take" or "what fields does this output have".</summary>
public sealed record DynamicValuesAnswer(
    string Connector,
    string Operation,
    string Name,
    string Kind,
    string Status,
    string Message,
    IReadOnlyList<AllowedValue>? Values,
    IReadOnlyList<ParameterView>? Fields,
    IReadOnlyList<OutputView>? Outputs,
    IReadOnlyList<string>? Missing,
    string? ConnectionReference,
    string? ConnectionOwner);

/// <summary>What a <c>parameter get</c> or <c>output get</c> command asked for.</summary>
internal sealed record DynamicValuesRequest(
    string? Profile,
    string Connector,
    string Operation,
    string Name,
    bool IsOutput,
    IReadOnlyList<string> Values,
    string? ConnectionReference,
    bool UseConnection,
    string? Search);

/// <summary>
/// Answers a dynamic-values question in one call: decides from the operation
/// spec what is still missing, picks the bound connection reference, asks for
/// consent before reading through it, and returns the values or fields.
/// </summary>
internal static class DynamicValuesQuery
{
    /// <returns>False when the request itself was invalid (exit code 2).</returns>
    public static async Task<bool> RunAsync(DynamicValuesRequest request, ILogger logger)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in request.Values)
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                logger.LogError("--value '{Value}' must have the form name=value.", pair);
                return false;
            }

            values[pair[..separator]] = pair[(separator + 1)..];
        }

        var kind = request.IsOutput ? "output" : "parameter";
        var metadata = TxcServices.Get<IConnectorMetadataService>();
        var operation = await metadata.GetOperationAsync(request.Profile, request.Connector, request.Operation, CancellationToken.None)
            .ConfigureAwait(false);
        var candidates = request.IsOutput ? operation.Outputs ?? [] : operation.Parameters;
        var target = candidates.FirstOrDefault(p => p.Name == request.Name);
        if (target is null)
        {
            logger.LogError(
                "'{Name}' is not {Kind} of {Operation}. Its {Kind}s: {Names}.",
                request.Name, request.IsOutput ? "an output" : "a parameter", request.Operation, kind,
                string.Join(", ", candidates.Select(p => p.Name)));
            return false;
        }

        ConnectionReferenceRecord? reference = null;
        string? owner = null;

        DynamicValuesAnswer Answer(string status, string message,
            IReadOnlyList<AllowedValue>? found = null, IReadOnlyList<ParameterView>? fields = null,
            IReadOnlyList<OutputView>? outputs = null, IReadOnlyList<string>? missing = null)
            => new(request.Connector, request.Operation, request.Name, kind, status, message,
                Filter(found, v => $"{v.Value} {v.Label}", request.Search),
                Filter(fields, f => $"{f.Name} {f.Description}", request.Search),
                Filter(outputs, o => $"{o.Key} {o.Description}", request.Search),
                missing, reference?.LogicalName, owner);

        var view = ParameterViews.From(operation, target);
        var dynamicKind = request.IsOutput
            ? target.DynamicSchema is null ? null : ParameterSource.Fields
            : view.ValuesFrom;

        if (dynamicKind is not (ParameterSource.List or ParameterSource.Fields))
        {
            Write(Answer(dynamicKind == ParameterSource.Picker ? DynamicValuesStatus.Unavailable : DynamicValuesStatus.Static,
                dynamicKind == ParameterSource.Picker
                    ? "This is a folder or file picker; choose the value with the user."
                    : view.AllowedValues is { Count: > 0 } ? "Static values from the operation spec." : "Nothing to look up.",
                found: view.AllowedValues));
            return true;
        }

        // Inputs the lookup needs are known from the spec alone: report them
        // before anything touches a connection.
        var dependsOn = request.IsOutput ? ParameterViews.Dependencies(target.DynamicSchema!) : view.DependsOn ?? [];
        var missing = dependsOn.Where(d => !values.ContainsKey(d)).ToList();
        if (missing.Count > 0)
        {
            var how = missing.Select(m => operation.Parameters.FirstOrDefault(p => p.Name == m) is { } p
                && ParameterViews.From(operation, p).GetValues is { } command
                    ? command
                    : $"choose '{m}' yourself");
            Write(Answer(DynamicValuesStatus.NeedsValues,
                $"Choose {string.Join(" and ", missing.Select(m => $"'{m}'"))} first and pass them with --value. " +
                $"Get their values with: {string.Join("; ", how)}",
                missing: missing));
            return true;
        }

        (reference, var error) = await PickConnectionReferenceAsync(request, metadata).ConfigureAwait(false);
        if (reference is null)
        {
            logger.LogError("{Error}", error);
            return false;
        }

        var resolution = await metadata.ResolveOperationAsync(
            request.Profile, request.Connector, request.Operation, reference.LogicalName, reference.ConnectionId!, values,
            request.IsOutput ? null : request.Name, request.IsOutput ? request.Name : null, request.UseConnection,
            CancellationToken.None).ConfigureAwait(false);

        owner = Owner(resolution.Connection);
        var lookup = resolution.Lookups.FirstOrDefault();

        if (lookup is null || lookup.Status == DynamicLookupStatus.Skipped)
        {
            Write(Answer(lookup?.MissingValues is { Count: > 0 } ? DynamicValuesStatus.NeedsValues : DynamicValuesStatus.Unavailable,
                lookup?.Reason ?? "These values cannot be looked up.", missing: lookup?.MissingValues));
        }
        else if (!resolution.ConnectionUsed)
        {
            Write(Answer(DynamicValuesStatus.NeedsConsent,
                $"Reading this uses connection reference '{reference.LogicalName}' ({reference.DisplayName}), owned by {owner ?? "an unknown user"}: " +
                "a read-only metadata lookup made as that person; nothing is created or changed. " +
                "Ask the user, and if they agree re-run with --use-connection."));
        }
        else if (lookup.Status != DynamicLookupStatus.Resolved)
        {
            Write(Answer(DynamicValuesStatus.Unavailable, $"The lookup failed: {lookup.Reason}"));
        }
        else if (request.IsOutput)
        {
            var fields = OutputViews.From(resolution.Operation)
                .Where(o => o.Key.StartsWith(request.Name + "/", StringComparison.Ordinal)
                    || o.Key.StartsWith(request.Name + "[]/", StringComparison.Ordinal))
                .ToList();
            Write(Answer(DynamicValuesStatus.Fields,
                $"{fields.Count} fields; read them with the expression shown.", outputs: fields));
        }
        else if (dynamicKind == ParameterSource.Fields)
        {
            var fields = ParameterViews.From(resolution.Operation)
                .Where(f => f.Name.StartsWith(request.Name + "/", StringComparison.Ordinal))
                .ToList();
            Write(Answer(DynamicValuesStatus.Fields,
                $"{fields.Count} fields; use them as inputs.parameters keys exactly as named.", fields: fields));
        }
        else
        {
            var resolved = ParameterViews.From(resolution.Operation, resolution.Operation.Parameters.First(p => p.Name == request.Name));
            Write(Answer(DynamicValuesStatus.Values,
                $"{resolved.AllowedValues?.Count ?? 0} values; use the value, not the label.", found: resolved.AllowedValues));
        }

        return true;
    }

    private static IReadOnlyList<T>? Filter<T>(IReadOnlyList<T>? items, Func<T, string> text, string? search)
        => items is null || string.IsNullOrWhiteSpace(search)
            ? items
            : items.Where(i => text(i).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

    internal static string? Owner(ConnectionDetail connection)
        => connection.OwnerName is { } name
            ? connection.OwnerPrincipalName is { } upn ? $"{name} <{upn}>" : name
            : null;

    private static async Task<(ConnectionReferenceRecord? Reference, string? Error)> PickConnectionReferenceAsync(
        DynamicValuesRequest request, IConnectorMetadataService metadata)
    {
        var references = await TxcServices.Get<IConnectionReferenceService>()
            .ListAsync(request.Profile, request.Connector, CancellationToken.None).ConfigureAwait(false);
        var bound = references.Where(r => !string.IsNullOrWhiteSpace(r.ConnectionId)).ToList();

        if (!string.IsNullOrWhiteSpace(request.ConnectionReference))
        {
            var named = references.FirstOrDefault(r => string.Equals(r.LogicalName, request.ConnectionReference, StringComparison.OrdinalIgnoreCase));
            if (named is null)
                return (null, $"Connection reference '{request.ConnectionReference}' for '{request.Connector}' does not exist. Available: {(references.Count == 0 ? "none" : string.Join(", ", references.Select(r => r.LogicalName)))}.");
            if (string.IsNullOrWhiteSpace(named.ConnectionId))
                return (null, $"Connection reference '{named.LogicalName}' is not bound to a connection. Ask the user to bind one.");
            return (named, null);
        }

        if (bound.Count == 1)
            return (bound[0], null);

        if (bound.Count == 0)
            return (null, $"No connection reference for '{request.Connector}' is bound to a connection. Ask the user to create one (in a solution: New > More > Connection reference) and run this again.");

        // Describe each so the user can actually choose.
        var described = new List<string>();
        foreach (var r in bound)
        {
            var connection = await metadata.GetConnectionAsync(request.Profile, request.Connector, r.ConnectionId!, CancellationToken.None)
                .ConfigureAwait(false);
            described.Add($"{r.LogicalName} (\"{r.DisplayName}\", owned by {Owner(connection) ?? "unknown"}, {connection.Status ?? "status unknown"})");
        }

        return (null, $"'{request.Connector}' has several bound connection references: {string.Join("; ", described)}. " +
            "Ask the user which one the flow should use and pass --connection-reference <logical name>.");
    }

    private static void Write(DynamicValuesAnswer answer) => OutputFormatter.WriteData(answer, Print);

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void Print(DynamicValuesAnswer answer)
    {
        OutputWriter.WriteLine($"{answer.Operation} {answer.Kind} {answer.Name}: {answer.Status}");
        OutputWriter.WriteLine(answer.Message);

        if (answer.Values is { Count: > 0 } found)
        {
            OutputWriter.WriteLine(string.Empty);
            var width = Math.Max(found.Max(v => v.Value.Length), 5);
            foreach (var v in found)
                OutputWriter.WriteLine(v.Label is null || v.Label == v.Value ? v.Value : $"{v.Value.PadRight(width)} | {v.Label}");
        }

        if (answer.Fields is { Count: > 0 } fields)
        {
            OutputWriter.WriteLine(string.Empty);
            OperationTextRenderer.PrintParameters(fields);
        }

        if (answer.Outputs is { Count: > 0 } outputs)
        {
            OutputWriter.WriteLine(string.Empty);
            OperationTextRenderer.PrintOutputs(outputs);
        }
    }
#pragma warning restore TXC003
}
