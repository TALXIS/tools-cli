using Microsoft.TemplateEngine.Abstractions;

namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Evaluates template parameters' <c>isEnabled</c> conditions against supplied values, so the parameter
/// listing can hide parameters that don't apply to them.
/// </summary>
public static class TemplateParameterConditionEvaluator
{
    /// <summary>
    /// Returns the names of the parameters enabled for <paramref name="providedValues"/>. Conditions see each
    /// parameter's supplied value, else its default, else empty. A parameter without a condition, or with one
    /// that can't be parsed, stays enabled.
    /// </summary>
    public static IReadOnlyList<string> SelectEnabled(
        IReadOnlyList<(string Name, string? EnabledCondition, string? DefaultValue)> parameters,
        IReadOnlyDictionary<string, string> providedValues)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parameters)
        {
            variables[p.Name] = providedValues.TryGetValue(p.Name, out var supplied) ? supplied : p.DefaultValue ?? "";
        }
        foreach (var kv in providedValues)
        {
            variables[kv.Key] = kv.Value;
        }

        var enabled = new List<string>();
        foreach (var p in parameters)
        {
            if (string.IsNullOrWhiteSpace(p.EnabledCondition)
                || TemplateConditionExpression.TryEvaluate(p.EnabledCondition, variables) != false)
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
