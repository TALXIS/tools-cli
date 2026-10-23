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

internal static class ParameterViews
{
    private const string ParameterReferencePrefix = "$ref:";

    public static IReadOnlyList<ParameterView> From(OperationDetail operation)
        => operation.Parameters.Select(p => From(operation, p)).ToList();

    public static ParameterView From(OperationDetail operation, OperationParameter p)
    {
        var (source, reference) = p.DynamicSchema is { } schema ? (ParameterSource.Fields, schema)
            : p.DynamicValues is { } values ? (ParameterSource.List, values)
            : p.DynamicTree is { } tree ? (ParameterSource.Picker, tree)
            : ((string?)null, (DynamicValuesRef?)null);

        var dependsOn = reference?.Parameters?.Values
            .Where(v => v is not null && v.StartsWith(ParameterReferencePrefix, StringComparison.Ordinal))
            .Select(v => v![ParameterReferencePrefix.Length..])
            .Distinct(StringComparer.Ordinal)
            .ToList();

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

    public static string GetValuesCommand(string connector, string operation, string parameter, IEnumerable<string>? dependsOn)
    {
        var command = $"environment connector operation parameter get {connector} {operation} {Quote(parameter)}";
        foreach (var dependency in dependsOn ?? [])
            command += $" --value {Quote($"{dependency}=<{dependency}>")}";
        return command;
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
