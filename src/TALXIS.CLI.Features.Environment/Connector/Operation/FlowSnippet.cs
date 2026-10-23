using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// Builds the JSON a flow definition needs for one connector operation, in the
/// exact shape solution exports use, so an agent pastes it instead of
/// assembling type, host, connection binding and authentication by hand.
/// </summary>
/// <remarks>
/// Required parameters are always present: with the chosen value when one is
/// known, otherwise a <c>&lt;placeholder&gt;</c> saying what goes there
/// (listing the allowed values when there are few). Optional parameters are
/// left out; the parameter table lists them.
/// </remarks>
internal static partial class FlowSnippet
{
    private const int MaxValuesInPlaceholder = 12;

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonIdentifierRegex { get; }

    public static JsonObject Build(
        OperationDetail operation,
        string? connectionReference = null,
        IReadOnlyDictionary<string, string>? values = null)
    {
        var parameters = new JsonObject();
        foreach (var p in operation.Parameters)
        {
            if (values is not null && values.TryGetValue(p.Name, out var chosen))
                parameters[p.Name] = Literal(chosen, p.Type);
            else if (p.Required && p.DynamicSchema is not null)
                parameters[p.Name + "/..."] = "<fields depend on other choices: see this parameter's getValues command>";
            else if (p.Required)
                parameters[p.Name] = p.DefaultValue is { } fallback ? Literal(fallback, p.Type) : Placeholder(p);
        }

        var step = new JsonObject
        {
            ["type"] = operation.ActionType,
            ["inputs"] = new JsonObject
            {
                ["host"] = new JsonObject
                {
                    ["apiId"] = operation.ApiId,
                    ["connectionName"] = operation.Connector,
                    ["operationId"] = operation.OperationId,
                },
                ["parameters"] = parameters,
                ["authentication"] = "@parameters('$authentication')",
            },
        };

        if (!operation.IsTrigger)
            step["runAfter"] = new JsonObject();

        return new JsonObject
        {
            ["connectionReferences"] = new JsonObject
            {
                [operation.Connector] = new JsonObject
                {
                    ["runtimeSource"] = "embedded",
                    ["connection"] = new JsonObject
                    {
                        ["connectionReferenceLogicalName"] = connectionReference ?? "<connection reference logical name: see 'environment connection-reference list'>",
                    },
                    ["api"] = new JsonObject { ["name"] = operation.Connector },
                },
            },
            [operation.IsTrigger ? "triggers" : "actions"] = new JsonObject
            {
                [StepName(operation)] = step,
            },
        };
    }

    private static string StepName(OperationDetail operation)
    {
        var name = NonIdentifierRegex.Replace(operation.Summary ?? operation.OperationId, "_").Trim('_');
        return string.IsNullOrEmpty(name) ? operation.OperationId : name;
    }

    private static JsonNode Placeholder(OperationParameter p)
    {
        var what = p.Description ?? p.Name;
        if (p.AllowedValues is { Count: > 0 and <= MaxValuesInPlaceholder } allowed)
        {
            var options = allowed.Select(v => p.ValueLabels?.TryGetValue(v, out var label) == true ? $"{v} ({label})" : v);
            return $"<{what}: one of {string.Join(", ", options)}>";
        }

        return $"<{what}>";
    }

    /// <summary>Integers and booleans go in as JSON literals, as exports have them.</summary>
    private static JsonNode? Literal(string value, string? type)
        => type switch
        {
            "integer" when long.TryParse(value, out var number) => JsonValue.Create(number),
            "number" when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var real) => JsonValue.Create(real),
            "boolean" when bool.TryParse(value, out var flag) => JsonValue.Create(flag),
            _ => JsonValue.Create(value),
        };
}
