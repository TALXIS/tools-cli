using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Evaluates a template parameter's <c>isEnabled</c> condition (e.g.
/// <c>(AttributeType == "Text")</c>) against a set of supplied values, so the parameter
/// listing can hide type-specific parameters that don't apply to the chosen values.
/// </summary>
public static class TemplateParameterConditionEvaluator
{
    /// <summary>
    /// Returns the names of the parameters that are enabled given <paramref name="providedValues"/>.
    /// A parameter with no condition is always enabled. Each condition is evaluated against a
    /// variable set built from every parameter's effective value (supplied value, else its default,
    /// else empty), mirroring how the engine resolves disabled parameters to their defaults.
    /// </summary>
    public static IReadOnlyList<string> SelectEnabled(
        IReadOnlyList<(string Name, string? EnabledCondition, string? DefaultValue)> parameters,
        IReadOnlyDictionary<string, string> providedValues)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parameters)
            variables[p.Name] = providedValues.TryGetValue(p.Name, out var supplied)
                ? supplied
                : p.DefaultValue ?? "";
        // Include any supplied keys that aren't declared parameters so conditions referencing
        // them still resolve.
        foreach (var kv in providedValues)
            variables[kv.Key] = kv.Value;

        var enabled = new List<string>();
        foreach (var p in parameters)
        {
            if (string.IsNullOrWhiteSpace(p.EnabledCondition)
                || TemplateConditionExpression.Evaluate(p.EnabledCondition!, variables))
            {
                enabled.Add(p.Name);
            }
        }
        return enabled;
    }

    /// <summary>
    /// Filters template parameters down to those enabled for the supplied values.
    /// </summary>
    public static IReadOnlyList<ITemplateParameter> FilterEnabled(
        IReadOnlyList<ITemplateParameter> parameters,
        IReadOnlyDictionary<string, string> providedValues)
    {
        var enabledNames = SelectEnabled(
            parameters.Select(p => (p.Name, p.Precedence?.IsEnabledCondition, p.DefaultValue?.ToString())).ToList(),
            providedValues).ToHashSet(StringComparer.Ordinal);

        return parameters.Where(p => enabledNames.Contains(p.Name)).ToList();
    }
}
