using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerApps;

/// <summary>
/// Distills a connector's OpenAPI 2.0 document into the small operation and
/// parameter model that flow-authoring agents need. Body schemas are flattened
/// into slash-joined leaf names (<c>item/subject</c>) because that is the key
/// format cloud flow definitions use in <c>inputs.parameters</c>.
/// </summary>
internal static class ConnectorSwaggerParser
{
    private static readonly string[] HttpVerbs = ["get", "put", "post", "delete", "patch", "head", "options"];

    // Body schemas can nest arbitrarily; flow parameter keys rarely go deeper.
    private const int MaxFlattenDepth = 4;

    // The connector API emits the -list/-properties spellings, hand-written swaggers the older ones.
    private static readonly string[] DynamicValuesExtensions = ["x-ms-dynamic-list", "x-ms-dynamic-values", "x-ms-dynamic-tree"];
    private static readonly string[] DynamicSchemaExtensions = ["x-ms-dynamic-properties", "x-ms-dynamic-schema"];

    public static IReadOnlyList<ConnectorOperationSummary> ListOperations(JsonElement swagger)
    {
        var operations = new List<ConnectorOperationSummary>();

        foreach (var (_, pathItem, _, operation) in EnumerateOperations(swagger))
        {
            if (!TryReadString(operation, "operationId", out var operationId))
                continue;

            operations.Add(new ConnectorOperationSummary(
                OperationId: operationId,
                Kind: ResolveKind(pathItem, operation),
                Summary: TryReadOptionalString(operation, "summary"),
                Visibility: TryReadOptionalString(operation, "x-ms-visibility"),
                Description: TryReadOptionalString(operation, "description")));
        }

        return operations;
    }

    public static ConnectorOperationDetail? GetOperation(
        JsonElement swagger,
        string connectorName,
        string apiId,
        string operationId)
    {
        foreach (var (path, pathItem, verb, operation) in EnumerateOperations(swagger))
        {
            if (!TryReadString(operation, "operationId", out var candidate)
                || !string.Equals(candidate, operationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new ConnectorOperationDetail(
                ConnectorName: connectorName,
                ApiId: apiId,
                OperationId: candidate,
                Kind: ResolveKind(pathItem, operation),
                Summary: TryReadOptionalString(operation, "summary"),
                Description: TryReadOptionalString(operation, "description"),
                HttpMethod: verb.ToUpperInvariant(),
                Path: path,
                Parameters: CollectParameters(swagger, pathItem, operation));
        }

        return null;
    }

    private static IEnumerable<(string Path, JsonElement PathItem, string Verb, JsonElement Operation)> EnumerateOperations(
        JsonElement swagger)
    {
        if (!swagger.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var pathProperty in paths.EnumerateObject())
        {
            if (pathProperty.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var verb in HttpVerbs)
            {
                if (pathProperty.Value.TryGetProperty(verb, out var operation)
                    && operation.ValueKind == JsonValueKind.Object)
                {
                    yield return (pathProperty.Name, pathProperty.Value, verb, operation);
                }
            }
        }
    }

    private static string ResolveKind(JsonElement pathItem, JsonElement operation)
    {
        if (!operation.TryGetProperty("x-ms-trigger", out _))
            return "action";

        // Webhook triggers register a callback URL instead of polling.
        return pathItem.TryGetProperty("x-ms-notification-content", out _)
            || operation.TryGetProperty("x-ms-notification-content", out _)
            ? "webhook-trigger"
            : "trigger";
    }

    private static IReadOnlyList<ConnectorOperationParameter> CollectParameters(
        JsonElement swagger,
        JsonElement pathItem,
        JsonElement operation)
    {
        var parameters = new List<ConnectorOperationParameter>();

        foreach (var parameter in EnumerateDeclaredParameters(swagger, pathItem, operation))
        {
            if (IsInternal(parameter))
                continue;

            var location = TryReadOptionalString(parameter, "in") ?? "query";
            if (!string.Equals(location, "body", StringComparison.OrdinalIgnoreCase))
            {
                parameters.Add(new ConnectorOperationParameter(
                    Name: TryReadOptionalString(parameter, "name") ?? string.Empty,
                    In: location,
                    Type: ReadTypeLabel(parameter),
                    Required: ReadRequiredFlag(parameter),
                    Summary: TryReadOptionalString(parameter, "x-ms-summary"),
                    Description: TryReadOptionalString(parameter, "description"),
                    EnumValues: ReadEnumValues(parameter),
                    IsDynamic: HasDynamicExtension(parameter),
                    DynamicSource: ReadDynamicSource(parameter)));
                continue;
            }

            FlattenBodyParameter(swagger, parameter, parameters);
        }

        return parameters;
    }

    private static IEnumerable<JsonElement> EnumerateDeclaredParameters(
        JsonElement swagger,
        JsonElement pathItem,
        JsonElement operation)
    {
        // Swagger 2.0: an operation-level parameter overrides a pathItem-level
        // one with the same (name, in) pair.
        var merged = new List<JsonElement>();
        var indexByKey = new Dictionary<(string Name, string In), int>();

        foreach (var owner in new[] { pathItem, operation })
        {
            if (!owner.TryGetProperty("parameters", out var declared) || declared.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var parameter in declared.EnumerateArray())
            {
                var resolved = ResolveRef(swagger, parameter);
                if (resolved.ValueKind != JsonValueKind.Object)
                    continue;

                var key = (
                    TryReadOptionalString(resolved, "name") ?? string.Empty,
                    TryReadOptionalString(resolved, "in") ?? "query");
                if (indexByKey.TryGetValue(key, out var existing))
                {
                    merged[existing] = resolved;
                }
                else
                {
                    indexByKey[key] = merged.Count;
                    merged.Add(resolved);
                }
            }
        }

        return merged;
    }

    private static void FlattenBodyParameter(
        JsonElement swagger,
        JsonElement parameter,
        List<ConnectorOperationParameter> parameters)
    {
        var name = TryReadOptionalString(parameter, "name") ?? "body";
        var required = ReadRequiredFlag(parameter);

        if (!parameter.TryGetProperty("schema", out var schema))
            return;

        var resolved = ResolveRef(swagger, schema);

        // Dynamic schemas depend on another parameter's value (e.g. the picked
        // entity); surface a single marker parameter instead of empty output.
        if (HasDynamicSchema(resolved) || HasDynamicSchema(parameter))
        {
            parameters.Add(new ConnectorOperationParameter(
                name, "body", "dynamic", required,
                TryReadOptionalString(parameter, "x-ms-summary"),
                TryReadOptionalString(parameter, "description"),
                EnumValues: null,
                IsDynamic: true,
                DynamicSource: ReadDynamicSource(resolved) ?? ReadDynamicSource(parameter)));
            return;
        }

        // Real flow definitions key body values as {bodyParamName}/{leaf}
        // (e.g. item/subject, emailMessage/To), so the flatten prefix starts
        // with the body parameter name.
        var countBeforeFlatten = parameters.Count;
        FlattenSchema(swagger, resolved, prefix: name, required, parameters, depth: 0);

        if (parameters.Count == countBeforeFlatten)
        {
            parameters.Add(new ConnectorOperationParameter(
                name, "body", ReadTypeLabel(resolved), required,
                TryReadOptionalString(parameter, "x-ms-summary"),
                TryReadOptionalString(parameter, "description"),
                ReadEnumValues(resolved),
                HasDynamicExtension(resolved),
                ReadDynamicSource(resolved)));
        }
    }

    private static void FlattenSchema(
        JsonElement swagger,
        JsonElement schema,
        string? prefix,
        bool ancestorsRequired,
        List<ConnectorOperationParameter> parameters,
        int depth)
    {
        if (depth > MaxFlattenDepth)
            return;

        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var branch in allOf.EnumerateArray())
            {
                var resolvedBranch = ResolveRef(swagger, branch);
                if (resolvedBranch.ValueKind == JsonValueKind.Object)
                    FlattenSchema(swagger, resolvedBranch, prefix, ancestorsRequired, parameters, depth + 1);
            }
        }

        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return;

        var requiredNames = ReadRequiredNames(schema);

        foreach (var property in properties.EnumerateObject())
        {
            var propertySchema = ResolveRef(swagger, property.Value);
            if (propertySchema.ValueKind != JsonValueKind.Object || IsInternal(propertySchema) || IsReadOnly(propertySchema))
                continue;

            var name = prefix is null ? property.Name : $"{prefix}/{property.Name}";
            var required = ancestorsRequired && requiredNames.Contains(property.Name);
            var isDynamicSchema = HasDynamicSchema(propertySchema);

            if (!isDynamicSchema && IsFlattenable(propertySchema) && depth < MaxFlattenDepth)
            {
                FlattenSchema(swagger, propertySchema, name, required, parameters, depth + 1);
                continue;
            }

            var truncated = !isDynamicSchema && IsFlattenable(propertySchema);
            parameters.Add(new ConnectorOperationParameter(
                Name: name,
                In: "body",
                Type: isDynamicSchema ? "dynamic" : truncated ? "object (truncated)" : ReadTypeLabel(propertySchema),
                Required: required,
                Summary: TryReadOptionalString(propertySchema, "x-ms-summary"),
                Description: TryReadOptionalString(propertySchema, "description"),
                EnumValues: ReadEnumValues(propertySchema),
                IsDynamic: isDynamicSchema || HasDynamicExtension(propertySchema),
                DynamicSource: ReadDynamicSource(propertySchema)));
        }
    }

    private static JsonElement ResolveRef(JsonElement swagger, JsonElement element)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("$ref", out var refElement)
            && refElement.ValueKind == JsonValueKind.String)
        {
            var reference = refElement.GetString();
            if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("#/", StringComparison.Ordinal) || !visited.Add(reference))
                return element;

            var current = swagger;
            foreach (var segment in reference[2..].Split('/'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    return element;
            }

            element = current;
        }

        return element;
    }

    private static bool IsFlattenable(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object
            && ((schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                || (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array));

    private static bool IsInternal(JsonElement element)
        => string.Equals(TryReadOptionalString(element, "x-ms-visibility"), "internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnly(JsonElement element)
        => element.TryGetProperty("readOnly", out var readOnly) && readOnly.ValueKind == JsonValueKind.True;

    private static bool ReadRequiredFlag(JsonElement parameter)
        => parameter.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True;

    private static HashSet<string> ReadRequiredNames(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String)
                    names.Add(name.GetString()!);
            }
        }

        return names;
    }

    private static string? ReadTypeLabel(JsonElement element)
    {
        var type = TryReadOptionalString(element, "type");
        var format = TryReadOptionalString(element, "format");
        return type is null ? null : format is null ? type : $"{type} ({format})";
    }

    private static IReadOnlyList<string>? ReadEnumValues(JsonElement element)
    {
        if (!element.TryGetProperty("enum", out var enumElement) || enumElement.ValueKind != JsonValueKind.Array)
            return null;

        var values = enumElement.EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText())
            .ToList();
        return values.Count > 0 ? values : null;
    }

    private static bool HasDynamicExtension(JsonElement element)
        => HasAnyProperty(element, DynamicValuesExtensions);

    private static bool HasDynamicSchema(JsonElement element)
        => HasAnyProperty(element, DynamicSchemaExtensions);

    private static bool HasAnyProperty(JsonElement element, string[] names)
        => element.ValueKind == JsonValueKind.Object && names.Any(name => element.TryGetProperty(name, out _));

    private static ConnectorDynamicSource? ReadDynamicSource(JsonElement element)
        => ReadDynamicSource(element, "schema", DynamicSchemaExtensions)
            ?? ReadDynamicSource(element, "values", DynamicValuesExtensions);

    private static ConnectorDynamicSource? ReadDynamicSource(JsonElement element, string kind, string[] extensions)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var extension in extensions)
        {
            if (!element.TryGetProperty(extension, out var definition) || definition.ValueKind != JsonValueKind.Object)
                continue;

            // x-ms-dynamic-tree nests its lookup under "open".
            if (definition.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.Object)
                definition = open;

            if (TryReadString(definition, "operationId", out var operationId))
                return new ConnectorDynamicSource(kind, operationId, ReadDynamicArguments(definition));
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadDynamicArguments(JsonElement definition)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!definition.TryGetProperty("parameters", out var declared) || declared.ValueKind != JsonValueKind.Object)
            return arguments;

        foreach (var argument in declared.EnumerateObject())
            arguments[argument.Name] = FormatDynamicArgument(argument.Value);

        return arguments;
    }

    private static string FormatDynamicArgument(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (TryReadString(value, "parameter", out var reference) || TryReadString(value, "parameterReference", out reference))
                return $"$ref:{reference}";

            if (value.TryGetProperty("value", out var literal))
                value = literal;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    }

    private static bool TryReadString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String)
            return false;

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return true;
    }

    private static string? TryReadOptionalString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var propertyElement)
           && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()?.Trim()
            : null;
}
