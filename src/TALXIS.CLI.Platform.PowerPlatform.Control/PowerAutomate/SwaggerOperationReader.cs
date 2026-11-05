using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Reads connector metadata from a raw Swagger 2.0 document, giving the same
/// <see cref="OperationDetail"/> the environment's <c>apiOperations</c> route
/// gives for the same operation.
/// </summary>
/// <remarks>
/// The document is rewritten into the environment's shape and then read by
/// <see cref="OperationSchemaReader"/>, so key flattening and the dynamic
/// extensions are read by one implementation for both sources:
/// <list type="bullet">
/// <item>path, query, header and form parameters become inputs by name;</item>
/// <item>the body parameter's schema is expanded under the parameter's name
/// (<c>body/recipient</c>), with every <c>$ref</c> resolved;</item>
/// <item>the success response's schema becomes the <c>body</c> output;</item>
/// <item><c>x-ms-trigger</c> marks a trigger, and <c>x-ms-notification-content</c>
/// (or a callback URL parameter) marks a webhook.</item>
/// </list>
/// </remarks>
internal sealed class SwaggerOperationReader : IConnectorDefinitionReader
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch", "head", "options"];

    /// <summary>Guards against <c>$ref</c> cycles; flow parameter keys stop at depth 4 anyway.</summary>
    private const int MaxRefDepth = 16;

    public ConnectorDetail ReadConnector(string swaggerJson, string connector, string? search)
    {
        var document = ParseDocument(swaggerJson);
        var envelope = new JsonObject
        {
            ["name"] = connector,
            ["properties"] = new JsonObject
            {
                ["displayName"] = (document["info"] as JsonObject)?["title"]?.DeepClone(),
                ["swagger"] = document,
            },
        };

        using var parsed = JsonDocument.Parse(envelope.ToJsonString());
        return SwaggerOperationIndexer.Index(parsed.RootElement, connector, search);
    }

    public OperationDetail ReadOperation(string swaggerJson, string connector, string operationId)
    {
        var document = ParseDocument(swaggerJson);
        var (pathItem, operation, spelledId) = FindOperation(document, operationId)
            ?? throw new ArgumentException($"The connector definition has no operation '{operationId}'.", nameof(operationId));

        var payload = new JsonObject { ["properties"] = ToEnvironmentShape(document, pathItem, operation) };
        using var parsed = JsonDocument.Parse(payload.ToJsonString());
        return OperationSchemaReader.Read(connector, spelledId, parsed.RootElement);
    }

    private static JsonObject ParseDocument(string swaggerJson)
    {
        try
        {
            return JsonNode.Parse(swaggerJson) as JsonObject
                ?? throw new ArgumentException("The connector definition is not a JSON object.", nameof(swaggerJson));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"The connector definition is not valid JSON: {ex.Message}", nameof(swaggerJson), ex);
        }
    }

    private static (JsonObject PathItem, JsonObject Operation, string OperationId)? FindOperation(JsonObject document, string operationId)
    {
        if (document["paths"] is not JsonObject paths)
            return null;

        foreach (var (_, pathNode) in paths)
        {
            if (pathNode is not JsonObject pathItem)
                continue;

            foreach (var (method, operationNode) in pathItem)
            {
                if (operationNode is JsonObject operation
                    && HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase)
                    && GetString(operation, "operationId") is { } id
                    && string.Equals(id, operationId, StringComparison.OrdinalIgnoreCase))
                {
                    return (pathItem, operation, id);
                }
            }
        }

        return null;
    }

    /// <summary>The <c>properties</c> of an <c>apiOperations/{operation}</c> payload.</summary>
    private static JsonObject ToEnvironmentShape(JsonObject document, JsonObject pathItem, JsonObject operation)
    {
        var properties = new JsonObject();
        CopyString(operation, "summary", properties);
        CopyString(operation, "description", properties);

        var annotation = Resolve(document, operation["x-ms-api-annotation"], depth: 0) as JsonObject;
        if (annotation is not null)
            properties["annotation"] = annotation;

        if ((operation["deprecated"] is JsonValue deprecated && deprecated.TryGetValue<bool>(out var flag) && flag)
            || string.Equals(annotation is null ? null : GetString(annotation, "status"), "Deprecated", StringComparison.OrdinalIgnoreCase))
        {
            properties["isDeprecated"] = true;
        }

        var parameters = ReadParameters(document, pathItem, operation);
        var notification = Resolve(document, operation["x-ms-notification-content"] ?? pathItem["x-ms-notification-content"], depth: 0) as JsonObject;
        var trigger = GetString(operation, "x-ms-trigger");
        if (trigger is not null)
            properties["trigger"] = trigger;

        // A webhook registers a callback URL instead of being polled: a trigger
        // that declares its notification payload, or an action that takes the URL.
        var takesCallback = parameters.Any(p => p["x-ms-notification-url"] is JsonValue v && v.TryGetValue<bool>(out var callback) && callback);
        if ((trigger is not null && notification is not null) || takesCallback)
            properties["operationType"] = OperationSchemaReader.OpenApiConnectionWebhook;

        properties["inputsDefinition"] = ReadInputs(document, parameters);

        var responseSchema = trigger is not null && notification is not null
            ? notification["schema"]
            : SuccessResponse(document, operation)?["schema"];
        if (Resolve(document, responseSchema, depth: 0) is JsonObject body)
        {
            // A file is returned as its content, which expressions read as a string.
            if (GetString(body, "type") == "file")
            {
                body["type"] = "string";
                body["format"] = "binary";
            }

            body["x-ms-property-name-alias"] = "body";
            properties["responsesDefinition"] = new JsonObject
            {
                ["200"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["body"] = body },
                },
            };
        }

        return properties;
    }

    /// <summary>
    /// Path-level parameters apply to every operation of the path; the
    /// operation's own declaration of the same name and location wins.
    /// </summary>
    private static List<JsonObject> ReadParameters(JsonObject document, JsonObject pathItem, JsonObject operation)
    {
        var result = new List<JsonObject>();
        foreach (var source in (ReadOnlySpan<JsonNode?>)[operation["parameters"], pathItem["parameters"]])
        {
            if (source is not JsonArray array)
                continue;

            foreach (var entry in array)
            {
                if (Resolve(document, entry, depth: 0) is not JsonObject parameter || GetString(parameter, "name") is not { } name)
                    continue;

                var location = GetString(parameter, "in");
                if (!result.Any(p => GetString(p, "name") == name && GetString(p, "in") == location))
                    result.Add(parameter);
            }
        }

        return result;
    }

    private static JsonObject ReadInputs(JsonObject document, List<JsonObject> parameters)
    {
        var inputs = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in parameters)
        {
            // The callback URL and the connection are filled in by the platform, never by the flow.
            var name = GetString(parameter, "name")!;
            if ((parameter["x-ms-notification-url"] is JsonValue v && v.TryGetValue<bool>(out var callback) && callback)
                || (name == "connectionId" && GetString(parameter, "in") == "path"))
            {
                continue;
            }

            JsonObject spec;
            if (GetString(parameter, "in") == "body")
            {
                spec = Resolve(document, parameter["schema"], depth: 0) as JsonObject ?? new JsonObject();
                foreach (var label in (ReadOnlySpan<string>)["x-ms-summary", "description", "x-ms-visibility"])
                {
                    if (!spec.ContainsKey(label) && parameter[label] is { } value)
                        spec[label] = value.DeepClone();
                }
            }
            else
            {
                spec = new JsonObject();
                foreach (var (key, value) in parameter)
                {
                    if (key is not ("name" or "in" or "required"))
                        spec[key] = value?.DeepClone();
                }
            }

            inputs[name] = spec;
            if (parameter["required"] is JsonValue isRequired && isRequired.TryGetValue<bool>(out var r) && r)
                required.Add(name);
        }

        return new JsonObject { ["type"] = "object", ["required"] = required, ["properties"] = inputs };
    }

    /// <summary>The first 2xx response, else <c>default</c>.</summary>
    private static JsonObject? SuccessResponse(JsonObject document, JsonObject operation)
    {
        if (operation["responses"] is not JsonObject responses)
            return null;

        var chosen = responses.FirstOrDefault(r => r.Key.StartsWith('2')).Value
            ?? responses.FirstOrDefault(r => r.Key == "default").Value;
        return Resolve(document, chosen, depth: 0) as JsonObject;
    }

    /// <summary>
    /// A deep copy of <paramref name="node"/> with every local <c>$ref</c>
    /// replaced by its target (sibling keys such as <c>x-ms-summary</c> win)
    /// and <c>allOf</c> merged into one schema.
    /// </summary>
    private static JsonNode? Resolve(JsonObject document, JsonNode? node, int depth)
    {
        if (node is null)
            return null;

        if (depth > MaxRefDepth)
            return new JsonObject { ["type"] = "object" };

        switch (node)
        {
            case JsonArray array:
                return new JsonArray(array.Select(item => Resolve(document, item, depth)).ToArray());

            case JsonObject obj:
                JsonObject result;
                if (GetString(obj, "$ref") is { } reference)
                {
                    result = Resolve(document, Lookup(document, reference), depth + 1) as JsonObject ?? new JsonObject();
                    foreach (var (key, value) in obj)
                    {
                        if (key != "$ref")
                            result[key] = Resolve(document, value, depth);
                    }
                }
                else
                {
                    result = new JsonObject();
                    foreach (var (key, value) in obj)
                    {
                        if (key != "allOf")
                            result[key] = Resolve(document, value, depth);
                    }
                }

                if (obj["allOf"] is JsonArray parts)
                    MergeAllOf(result, parts.Select(part => Resolve(document, part, depth + 1)).OfType<JsonObject>());

                return result;

            default:
                return node.DeepClone();
        }
    }

    private static void MergeAllOf(JsonObject target, IEnumerable<JsonObject> parts)
    {
        foreach (var part in parts)
        {
            foreach (var (key, value) in part)
            {
                if (key == "properties" && value is JsonObject members)
                {
                    var merged = target["properties"] as JsonObject ?? new JsonObject();
                    foreach (var (member, spec) in members)
                        merged[member] ??= spec?.DeepClone();
                    target["properties"] = merged;
                }
                else if (key == "required" && value is JsonArray names)
                {
                    var merged = target["required"] as JsonArray ?? new JsonArray();
                    foreach (var name in names.Select(n => n?.GetValue<string>()).OfType<string>())
                    {
                        if (!merged.Any(n => n?.GetValue<string>() == name))
                            merged.Add(name);
                    }

                    target["required"] = merged;
                }
                else if (!target.ContainsKey(key))
                {
                    target[key] = value?.DeepClone();
                }
            }
        }
    }

    /// <summary>Follows a local JSON pointer such as <c>#/definitions/Product</c>.</summary>
    private static JsonNode? Lookup(JsonObject document, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            return null;

        JsonNode? current = document;
        foreach (var segment in reference[2..].Split('/'))
        {
            var key = Uri.UnescapeDataString(segment).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = (current as JsonObject)?[key];
            if (current is null)
                return null;
        }

        return current;
    }

    private static string? GetString(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void CopyString(JsonObject source, string name, JsonObject target)
    {
        if (GetString(source, name) is { } value)
            target[name] = value;
    }
}
