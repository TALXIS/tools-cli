using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Works out which design-time lookups would resolve an operation's dynamic
/// parameters, and applies their answers back onto the parameter list.
/// </summary>
/// <remarks>
/// Only lookups named by the operation's own <c>x-ms-dynamic-*</c> metadata
/// are planned, and only GETs and POSTs: these are the calls the flow designer
/// makes while an action is being configured (some connectors, such as
/// Dataverse, declare their lookups as POSTs). Nothing else is ever sent
/// through the user's connection.
/// </remarks>
internal static class DynamicLookupPlanner
{
    public const string ValuesKind = "values";
    public const string SchemaKind = "schema";
    public const string TreeKind = "tree";

    private const string ParameterReferencePrefix = "$ref:";
    private const string OptionalReferencePrefix = "$ref?:";

    private const string BodyArgumentPrefix = "body/";

    private static readonly string[] AllowedMethods = ["GET", "POST"];

    /// <summary>The request to make for a lookup, or nulls when it is skipped.</summary>
    internal sealed record PlannedLookup(
        OperationParameter Parameter,
        DynamicValuesRef Reference,
        DynamicLookup Lookup,
        Uri? RequestUri,
        HttpMethod? Method = null,
        IReadOnlyDictionary<string, string>? Headers = null,
        JsonObject? Body = null);

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
            if (argument is not null && argument.StartsWith(OptionalReferencePrefix, StringComparison.Ordinal))
            {
                if (values.TryGetValue(argument[OptionalReferencePrefix.Length..], out var optional))
                    arguments[name] = optional;
            }
            else if (argument is not null && argument.StartsWith(ParameterReferencePrefix, StringComparison.Ordinal))
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
                new DynamicLookup(parameter.Name, kind, reference.OperationId, DynamicLookupStatus.Skipped, reason,
                    missing.Count > 0 ? missing : null),
                null);

        if (kind == TreeKind)
            return Skip("Folder and file pickers are browsed interactively; choose the path with the user.");
        if (found is null)
            return Skip("The lookup operation is not in the connector definition.");
        if (!AllowedMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
            return Skip($"The lookup is a {method}; only GET and POST lookups are made through a connection.");
        if (missing.Count > 0)
            return Skip("Needs a value for " + string.Join(", ", missing.Select(m => $"'{m}'")) + " first (pass --value <name>=<value>).");
        if (string.IsNullOrWhiteSpace(runtimeUrl))
            return Skip("The connector does not publish a runtime URL.");

        var request = BuildRequest(swagger, found.Value, connectionId, arguments, out var unfilled);
        if (unfilled is not null)
            return Skip($"The lookup needs '{unfilled}', which the operation metadata does not supply.");

        return new PlannedLookup(parameter, reference,
            new DynamicLookup(parameter.Name, kind, reference.OperationId, DynamicLookupStatus.Planned, null),
            new Uri(runtimeUrl!.TrimEnd('/') + request.Path),
            new HttpMethod(method),
            request.Headers.Count == 0 ? null : request.Headers,
            request.Body);
    }

    private sealed record LookupRequest(string Path, Dictionary<string, string> Headers, JsonObject? Body);

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
    /// Renders the lookup's request from its swagger: path parameters
    /// substituted, query parameters appended, declared headers set, and
    /// <c>body/…</c> arguments assembled into the JSON body.
    /// <c>connectionId</c> is the connection itself.
    /// </summary>
    private static LookupRequest BuildRequest(
        JsonElement swagger,
        SwaggerOperation operation,
        string connectionId,
        IReadOnlyDictionary<string, string?> arguments,
        out string? unfilled)
    {
        unfilled = null;
        var path = operation.Path;
        var query = new StringBuilder();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        JsonObject? body = null;

        if (operation.Definition.TryGetProperty("parameters", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            foreach (var raw in declared.EnumerateArray())
            {
                var parameter = ResolveRef(swagger, raw);
                var name = JsonRead.String(parameter, "name");
                var location = JsonRead.String(parameter, "in")?.ToLowerInvariant();
                if (name is null || location is null)
                    continue;

                if (location == "body")
                {
                    body = BuildBody(arguments);
                    continue;
                }

                var value = string.Equals(name, "connectionId", StringComparison.Ordinal)
                    ? connectionId
                    : arguments.TryGetValue(name, out var given) && given is not null
                        ? given
                        : parameter.TryGetProperty("default", out var fallback) ? ScalarText(fallback) : null;

                var required = parameter.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True;
                if (value is null)
                {
                    if (required || location == "path")
                    {
                        unfilled = name;
                        return new LookupRequest(path, headers, body);
                    }

                    continue;
                }

                switch (location)
                {
                    case "path":
                        path = path.Replace("{" + name + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
                        break;
                    case "query":
                        query.Append(query.Length == 0 ? '?' : '&')
                            .Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
                        break;
                    case "header":
                        headers[name] = value;
                        break;
                }
            }
        }

        return new LookupRequest(path + query, headers, body);
    }

    /// <summary>
    /// Nests <c>body/a/b=value</c> arguments into <c>{ "a": { "b": "value" } }</c>,
    /// the same flattening flow definitions use, reversed.
    /// </summary>
    private static JsonObject BuildBody(IReadOnlyDictionary<string, string?> arguments)
    {
        var body = new JsonObject();
        foreach (var (name, value) in arguments)
        {
            if (!name.StartsWith(BodyArgumentPrefix, StringComparison.Ordinal) || value is null)
                continue;

            var segments = name[BodyArgumentPrefix.Length..].Split('/');
            var node = body;
            foreach (var segment in segments[..^1])
            {
                if (node[segment] is not JsonObject child)
                {
                    child = new JsonObject();
                    node[segment] = child;
                }

                node = child;
            }

            node[segments[^1]] = value;
        }

        return body;
    }

    /// <summary>Copies a lookup's answer onto the parameter list.</summary>
    /// <param name="isOutput">
    /// The list is outputs: read-only fields are kept, and an array's item
    /// fields are named under <c>{array}[]</c>.
    /// </param>
    public static IReadOnlyList<OperationParameter> Apply(
        IReadOnlyList<OperationParameter> parameters,
        PlannedLookup lookup,
        JsonElement response,
        bool isOutput = false)
    {
        var index = parameters.ToList().FindIndex(p => p.Name == lookup.Parameter.Name);
        if (index < 0)
            return parameters;

        var result = parameters.ToList();
        if (lookup.Lookup.Kind == SchemaKind)
        {
            var schema = Navigate(response, lookup.Reference.ValuePath) ?? response;
            var arrayItems = isOutput && lookup.Parameter.Type == "array";
            var prefix = arrayItems ? lookup.Parameter.Name + "[]" : lookup.Parameter.Name;
            var leaves = OperationSchemaReader.ReadSubSchema(schema, prefix, lookup.Parameter.Required, includeReadOnly: isOutput);
            if (leaves.Count > 0)
            {
                // An array stays listed (it is what a loop runs over); its
                // item fields follow it.
                if (arrayItems)
                    result[index] = result[index] with { DynamicSchema = null };
                else
                    result.RemoveAt(index);
                result.InsertRange(arrayItems ? index + 1 : index, leaves);
            }
        }
        else
        {
            var allowed = ReadValues(response, lookup.Reference);
            if (allowed.Count > 0)
            {
                var labels = allowed
                    .Where(v => v.Title is not null && v.Title != v.Value)
                    .GroupBy(v => v.Value)
                    .ToDictionary(g => g.Key, g => g.First().Title!);
                result[index] = result[index] with
                {
                    AllowedValues = allowed.Select(v => v.Value).ToList(),
                    ValueLabels = labels.Count == 0 ? null : labels,
                };
            }
        }

        return result;
    }

    private static IReadOnlyList<(string Value, string? Title)> ReadValues(JsonElement response, DynamicValuesRef reference)
    {
        var collection = Navigate(response, reference.ItemsPath)
            ?? (response.ValueKind == JsonValueKind.Array ? response : Navigate(response, "value"));

        if (collection is not { ValueKind: JsonValueKind.Array } items)
            return [];

        var values = new List<(string, string?)>();
        foreach (var item in items.EnumerateArray())
        {
            if (ScalarText(Navigate(item, reference.ValuePath) ?? item) is not { } value)
                continue;

            var title = Navigate(item, reference.TitlePath) is { } t ? ScalarText(t) : null;
            values.Add((value, title));
        }

        return values;
    }

    /// <summary>
    /// Follows a <c>a/b</c> or <c>a.b</c> path; null when any step is absent.
    /// Matching falls back to ignoring case: connector metadata names paths
    /// in PascalCase (<c>LogicalName</c>) while some responses are camelCase
    /// (<c>logicalName</c>), as the Dataverse lookups are.
    /// </summary>
    internal static JsonElement? Navigate(JsonElement element, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var current = element;
        foreach (var segment in path.Split('/', '.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (current.TryGetProperty(segment, out var exact))
            {
                current = exact;
                continue;
            }

            var match = current.EnumerateObject()
                .FirstOrDefault(p => string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (match.Value.ValueKind == JsonValueKind.Undefined)
                return null;

            current = match.Value;
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
