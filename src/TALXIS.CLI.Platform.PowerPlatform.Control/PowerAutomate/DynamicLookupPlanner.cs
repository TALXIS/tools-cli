using System.Text;
using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Works out which design-time lookups would resolve an operation's dynamic
/// parameters, and applies their answers back onto the parameter list.
/// </summary>
/// <remarks>
/// Only lookups named by the operation's own <c>x-ms-dynamic-*</c> metadata
/// are planned, and only GETs: these are the calls the flow designer makes
/// while an action is being configured, and nothing else is ever sent through
/// the user's connection.
/// </remarks>
internal static class DynamicLookupPlanner
{
    public const string ValuesKind = "values";
    public const string SchemaKind = "schema";
    public const string TreeKind = "tree";

    private const string ParameterReferencePrefix = "$ref:";

    internal sealed record PlannedLookup(OperationParameter Parameter, DynamicValuesRef Reference, DynamicLookup Lookup, Uri? RequestUri);

    public static IReadOnlyList<PlannedLookup> Plan(
        OperationDetail operation,
        JsonElement connectorPayload,
        string connectionId,
        IReadOnlyDictionary<string, string> values)
    {
        var properties = JsonRead.Object(connectorPayload, "properties") ?? default;
        var runtimeUrl = JsonRead.String(properties, "primaryRuntimeUrl")
            ?? (properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("runtimeUrls", out var urls)
                && urls.ValueKind == JsonValueKind.Array
                && urls.GetArrayLength() > 0
                    ? urls[0].GetString()
                    : null);
        var swagger = JsonRead.Object(properties, "swagger") ?? default;
        var lookups = new List<PlannedLookup>();

        foreach (var parameter in operation.Parameters)
        {
            foreach (var (kind, reference) in References(parameter))
                lookups.Add(PlanOne(parameter, kind, reference, swagger, runtimeUrl, connectionId, values));
        }

        return lookups;
    }

    private static IEnumerable<(string Kind, DynamicValuesRef Reference)> References(OperationParameter parameter)
    {
        if (parameter.DynamicValues is { } values)
            yield return (ValuesKind, values);
        if (parameter.DynamicSchema is { } schema)
            yield return (SchemaKind, schema);
        if (parameter.DynamicTree is { } tree)
            yield return (TreeKind, tree);
    }

    private static PlannedLookup PlanOne(
        OperationParameter parameter,
        string kind,
        DynamicValuesRef reference,
        JsonElement swagger,
        string? runtimeUrl,
        string connectionId,
        IReadOnlyDictionary<string, string> values)
    {
        var arguments = new Dictionary<string, string?>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var (name, argument) in reference.Parameters ?? new Dictionary<string, string?>())
        {
            if (argument is not null && argument.StartsWith(ParameterReferencePrefix, StringComparison.Ordinal))
            {
                var source = argument[ParameterReferencePrefix.Length..];
                if (values.TryGetValue(source, out var chosen))
                    arguments[name] = chosen;
                else
                    missing.Add(source);
            }
            else
            {
                arguments[name] = argument;
            }
        }

        var found = FindOperation(swagger, reference.OperationId);
        var method = found?.Method ?? "?";
        var path = found?.Path ?? string.Empty;

        PlannedLookup Skip(string reason)
            => new(parameter, reference,
                new DynamicLookup(parameter.Name, kind, reference.OperationId, method, path, arguments, DynamicLookupStatus.Skipped, reason),
                null);

        if (kind == TreeKind)
            return Skip("Folder and file pickers are browsed interactively; choose the path with the user.");
        if (found is null)
            return Skip("The lookup operation is not in the connector definition.");
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return Skip($"The lookup is a {method}; only GET lookups are made through a connection.");
        if (missing.Count > 0)
            return Skip("Needs a value for " + string.Join(", ", missing.Select(m => $"'{m}'")) + " first (pass --value <name>=<value>).");
        if (string.IsNullOrWhiteSpace(runtimeUrl))
            return Skip("The connector does not publish a runtime URL.");

        var request = BuildRequest(swagger, found.Value, connectionId, arguments, out var unfilled);
        if (unfilled is not null)
            return Skip($"The lookup needs '{unfilled}', which the operation metadata does not supply.");

        return new PlannedLookup(parameter, reference,
            new DynamicLookup(parameter.Name, kind, reference.OperationId, method, request, arguments, DynamicLookupStatus.Planned, null),
            new Uri(runtimeUrl!.TrimEnd('/') + request));
    }

    private readonly record struct SwaggerOperation(string Method, string Path, JsonElement Definition);

    private static SwaggerOperation? FindOperation(JsonElement swagger, string operationId)
    {
        if (JsonRead.Object(swagger, "paths") is not { } paths)
            return null;

        foreach (var path in paths.EnumerateObject())
        {
            if (path.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Value.ValueKind == JsonValueKind.Object
                    && string.Equals(JsonRead.String(method.Value, "operationId"), operationId, StringComparison.Ordinal))
                {
                    return new SwaggerOperation(method.Name.ToUpperInvariant(), path.Name, method.Value);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Renders the lookup's relative request: path parameters substituted,
    /// query parameters appended. <c>connectionId</c> is the connection itself.
    /// </summary>
    private static string BuildRequest(
        JsonElement swagger,
        SwaggerOperation operation,
        string connectionId,
        IReadOnlyDictionary<string, string?> arguments,
        out string? unfilled)
    {
        unfilled = null;
        var path = operation.Path;
        var query = new StringBuilder();

        if (operation.Definition.TryGetProperty("parameters", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            foreach (var raw in declared.EnumerateArray())
            {
                var parameter = ResolveRef(swagger, raw);
                var name = JsonRead.String(parameter, "name");
                var location = JsonRead.String(parameter, "in");
                if (name is null || location is null)
                    continue;

                var value = string.Equals(name, "connectionId", StringComparison.Ordinal)
                    ? connectionId
                    : arguments.TryGetValue(name, out var given) && given is not null
                        ? given
                        : parameter.TryGetProperty("default", out var fallback) ? ScalarText(fallback) : null;

                var required = parameter.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True;

                if (string.Equals(location, "path", StringComparison.OrdinalIgnoreCase))
                {
                    if (value is null)
                    {
                        unfilled = name;
                        return path;
                    }

                    path = path.Replace("{" + name + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
                }
                else if (string.Equals(location, "query", StringComparison.OrdinalIgnoreCase) && value is not null)
                {
                    query.Append(query.Length == 0 ? '?' : '&')
                        .Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
                }
                else if (string.Equals(location, "query", StringComparison.OrdinalIgnoreCase) && required)
                {
                    unfilled = name;
                    return path;
                }
            }
        }

        return path + query;
    }

    /// <summary>Copies a lookup's answer onto the parameter list.</summary>
    public static IReadOnlyList<OperationParameter> Apply(
        IReadOnlyList<OperationParameter> parameters,
        PlannedLookup lookup,
        JsonElement response)
    {
        var index = parameters.ToList().FindIndex(p => p.Name == lookup.Parameter.Name);
        if (index < 0)
            return parameters;

        var result = parameters.ToList();
        if (lookup.Lookup.Kind == SchemaKind)
        {
            var schema = Navigate(response, lookup.Reference.ValuePath) ?? response;
            var leaves = OperationSchemaReader.ReadSubSchema(schema, lookup.Parameter.Name, lookup.Parameter.Required);
            if (leaves.Count > 0)
            {
                result.RemoveAt(index);
                result.InsertRange(index, leaves);
            }
        }
        else
        {
            var allowed = ReadValues(response, lookup.Reference);
            if (allowed.Count > 0)
                result[index] = result[index] with { AllowedValues = allowed };
        }

        return result;
    }

    private static IReadOnlyList<string> ReadValues(JsonElement response, DynamicValuesRef reference)
    {
        var collection = Navigate(response, reference.ItemsPath)
            ?? (response.ValueKind == JsonValueKind.Array ? response : Navigate(response, "value"));

        if (collection is not { ValueKind: JsonValueKind.Array } items)
            return [];

        return items.EnumerateArray()
            .Select(item => ScalarText(Navigate(item, reference.ValuePath) ?? item))
            .OfType<string>()
            .ToList();
    }

    /// <summary>Follows a <c>a/b</c> or <c>a.b</c> path; null when any step is absent.</summary>
    internal static JsonElement? Navigate(JsonElement element, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var current = element;
        foreach (var segment in path.Split('/', '.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current;
    }

    private static JsonElement ResolveRef(JsonElement swagger, JsonElement element)
    {
        if (JsonRead.String(element, "$ref") is not { } reference || !reference.StartsWith("#/", StringComparison.Ordinal))
            return element;

        var current = swagger;
        foreach (var segment in reference[2..].Split('/'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return element;
        }

        return current;
    }

    private static string? ScalarText(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
}
