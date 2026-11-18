using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>An allowed value and what the designer shows for it.</summary>
public sealed record AllowedValue(string Value, string? Label);

/// <summary>Where a parameter's values come from.</summary>
public static class ParameterSource
{
    /// <summary>A list fetched at design time (e.g. table names).</summary>
    public const string List = "list";
    /// <summary>Sub-fields that depend on other choices (e.g. a Dataverse row, a Teams message body).</summary>
    public const string Fields = "fields";
    /// <summary>A folder or file picker; the value is chosen with the user.</summary>
    public const string Picker = "picker";
}

/// <summary>
/// One parameter as an agent needs it: what to put in it and, when its values
/// are not static, which other parameters they depend on and the one command
/// that fetches them. The connector's internal lookup operations are not shown.
/// </summary>
public sealed record ParameterView(
    string Name,
    string? Type,
    bool Required,
    string? Description,
    IReadOnlyList<AllowedValue>? AllowedValues,
    string? ValuesFrom,
    IReadOnlyList<string>? DependsOn,
    string? GetValues);

/// <summary>
/// One thing an operation returns, with the expression that reads it in a
/// later step. A dynamic output (fields that depend on choices, e.g. the
/// columns of the chosen table) says how to get its fields instead.
/// </summary>
public sealed record OutputView(
    string Key,
    string? Type,
    string? Description,
    string Expression,
    IReadOnlyList<string>? DependsOn,
    string? GetFields);

internal static class OutputViews
{
    public static IReadOnlyList<OutputView> From(OperationDetail operation)
        => (operation.Outputs ?? []).Select(o => From(operation, o)).ToList();

    public static OutputView From(OperationDetail operation, OperationParameter output)
    {
        var dependsOn = output.DynamicSchema is { } schema ? ParameterViews.Dependencies(schema) : null;
        return new OutputView(
            Key: output.Name,
            Type: output.Type,
            Description: output.Description,
            Expression: Expression(operation, output.Name),
            DependsOn: dependsOn is { Count: > 0 } ? dependsOn : null,
            GetFields: output.DynamicSchema is null
                ? null
                : ParameterViews.Command("output", operation.Connector, operation.OperationId, output.Name, dependsOn));
    }

    /// <summary>
    /// <c>@triggerOutputs()?['body/x']</c> for a trigger, <c>@outputs('Step')?['body/x']</c>
    /// for an action, <c>@items('Loop')?['x']</c> for a field of an array item.
    /// </summary>
    public static string Expression(OperationDetail operation, string key)
    {
        var item = key.IndexOf("[]/", StringComparison.Ordinal);
        if (item >= 0)
            return $"@items('<loop over {key[..item]}>')?['{key[(item + 3)..]}']";

        return operation.IsTrigger
            ? $"@triggerOutputs()?['{key}']"
            : $"@outputs('{FlowSnippet.StepName(operation)}')?['{key}']";
    }
}

internal static class ParameterViews
{
    public static IReadOnlyList<ParameterView> From(OperationDetail operation)
        => operation.Parameters.Select(p => From(operation, p)).ToList();

    public static ParameterView From(OperationDetail operation, OperationParameter p)
    {
        var (source, reference) = p.DynamicSchema is { } schema ? (ParameterSource.Fields, schema)
            : p.DynamicValues is { } values ? (ParameterSource.List, values)
            : p.DynamicTree is { } tree ? (ParameterSource.Picker, tree)
            : ((string?)null, (DynamicValuesRef?)null);

        var dependsOn = reference is null ? null : Dependencies(reference);

        return new ParameterView(
            Name: p.Name,
            Type: p.Type,
            Required: p.Required,
            Description: p.Description,
            AllowedValues: p.AllowedValues?.Select(v => new AllowedValue(v, p.ValueLabels?.GetValueOrDefault(v))).ToList(),
            ValuesFrom: source,
            DependsOn: dependsOn is { Count: > 0 } ? dependsOn : null,
            GetValues: source is ParameterSource.List or ParameterSource.Fields
                ? GetValuesCommand(operation.Connector, operation.OperationId, p.Name, dependsOn)
                : source == ParameterSource.Picker ? "Choose the folder or file with the user." : null);
    }

    /// <summary>The parameters of the operation a lookup takes as inputs.</summary>
    public static List<string> Dependencies(DynamicValuesRef reference)
        => (reference.Parameters?.Values ?? [])
            .Select(v => ResolverArgument.Referenced(v, out var optional) is { } name && !optional ? name : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static string GetValuesCommand(string connector, string operation, string parameter, IEnumerable<string>? dependsOn)
        => Command("parameter", connector, operation, parameter, dependsOn);

    /// <summary>A runnable command line: what the agent types next, as is.</summary>
    public static string Command(string noun, string connector, string operation, string name, IEnumerable<string>? dependsOn)
    {
        var command = $"txc environment connector operation {noun} get {connector} {operation} {Quote(name)}";
        foreach (var dependency in dependsOn ?? [])
            command += $" --value {Quote($"{dependency}=<{dependency}>")}";
        return command;
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
