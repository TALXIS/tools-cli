using System.Text.Json;
using System.Text.RegularExpressions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Reads the parameter specification for one connector operation from an
/// <c>apiOperations/{operation}</c> payload, and infers the action type a flow
/// definition must declare for it.
/// </summary>
/// <remarks>
/// Object inputs with a static schema are flattened into slash-joined leaf
/// names (<c>emailMessage/To</c>), because that is the key format flow
/// definitions use in <c>inputs.parameters</c>. Inputs whose sub-schema is
/// resolved at design time (<c>x-ms-dynamic-properties</c>, e.g. the Dataverse
/// <c>item</c>) stay a single entry carrying the resolver, since their leaf
/// names cannot be known from the operation spec.
/// </remarks>
internal static partial class OperationSchemaReader
{
    public const string OpenApiConnection = "OpenApiConnection";
    public const string OpenApiConnectionWebhook = "OpenApiConnectionWebhook";
    public const string OpenApiConnectionNotification = "OpenApiConnectionNotification";

    /// <summary>Body schemas can nest arbitrarily; flow parameter keys rarely go deeper.</summary>
    private const int MaxFlattenDepth = 4;

    /// <summary>
    /// Operations whose name follows this pattern are long-running webhook
    /// subscriptions even when the payload carries no annotation to say so.
    /// </summary>
    [GeneratedRegex("^(StartAndWait|WaitFor|Subscribe)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebhookOperationNameRegex { get; }

    public static OperationDetail Read(string connector, string operationId, JsonElement payload)
    {
        var properties = JsonElementReader.TryGetObject(payload, "properties") ?? payload;
        var inputs = JsonElementReader.TryGetObject(properties, "inputsDefinition");

        var parameters = new List<OperationParameter>();
        if (inputs is { } definition)
            ReadObject(definition, prefix: null, ancestorsRequired: true, parameters, depth: 0);

        return new OperationDetail(
            Connector: connector,
            ApiId: ConnectorIds.ToPath(connector),
            OperationId: operationId,
            Summary: JsonElementReader.TryGetString(properties, "summary"),
            Description: JsonElementReader.TryGetString(properties, "description"),
            ActionType: InferActionType(properties, operationId),
            IsDeprecated: IsDeprecated(properties),
            Parameters: parameters,
            IsTrigger: JsonElementReader.TryGetString(properties, "trigger") is not null,
            Outputs: ReadOutputs(JsonElementReader.TryGetObject(properties, "responsesDefinition")));
    }

    /// <summary>
    /// Reads what the operation returns, keyed by the paths expressions use.
    /// The first response is used: an operation declares one success shape
    /// (200, 201, or "webhook" for a webhook trigger).
    /// </summary>
    private static IReadOnlyList<OperationParameter> ReadOutputs(JsonElement? responses)
    {
        if (responses is not { ValueKind: JsonValueKind.Object } bag)
            return [];

        var first = bag.EnumerateObject().Select(r => r.Value).FirstOrDefault(v => v.ValueKind == JsonValueKind.Object);
        if (first.ValueKind != JsonValueKind.Object)
            return [];

        var outputs = new List<OperationParameter>();
        WalkOutputs(first, prefix: null, inArrayItem: false, outputs, depth: 0);
        return outputs;
    }

    private static void WalkOutputs(JsonElement schema, string? prefix, bool inArrayItem, List<OperationParameter> outputs, int depth)
    {
        if (JsonElementReader.TryGetObject(schema, "properties") is not { } members)
            return;

        foreach (var member in members.EnumerateObject())
        {
            var spec = member.Value;
            if (spec.ValueKind != JsonValueKind.Object
                || string.Equals(JsonElementReader.TryGetString(spec, "x-ms-visibility"), "internal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The alias is the expression key (body/value); inside an array
            // item it is relative to the item, so the path is built instead.
            var name = !inArrayItem && JsonElementReader.TryGetString(spec, "x-ms-property-name-alias") is { Length: > 0 } alias
                ? alias
                : prefix is null ? member.Name : $"{prefix}/{member.Name}";
            var type = JsonElementReader.TryGetString(spec, "type");

            if (type == "array" && JsonElementReader.TryGetObject(spec, "items") is { } items)
            {
                var itemSchema = ReadDynamicRef(items, "x-ms-dynamic-schema", "x-ms-dynamic-properties");
                outputs.Add(ReadParameter(name, spec, required: false) with { Type = "array", DynamicSchema = itemSchema });
                if (itemSchema is null && depth < MaxFlattenDepth)
                    WalkOutputs(items, name + "[]", inArrayItem: true, outputs, depth + 1);
            }
            else if (ReadDynamicRef(spec, "x-ms-dynamic-schema", "x-ms-dynamic-properties") is { } dynamic)
            {
                outputs.Add(ReadParameter(name, spec, required: false) with { Type = type ?? "object", DynamicSchema = dynamic });
            }
            else if (JsonElementReader.TryGetObject(spec, "properties") is not null && depth < MaxFlattenDepth)
            {
                WalkOutputs(spec, name, inArrayItem, outputs, depth + 1);
            }
            else
            {
                outputs.Add(ReadParameter(name, spec, required: false));
            }
        }
    }

    /// <summary>
    /// Determines the <c>type</c> a flow action must declare. An approval
    /// declared as a plain connection silently never resumes, so the
    /// annotation is preferred and the name pattern is only a fallback.
    /// </summary>
    public static string InferActionType(JsonElement properties, string operationId)
    {
        // The operation API states the type outright; the rest is for payloads without it.
        if (JsonElementReader.TryGetString(properties, "operationType") is { Length: > 0 } stated)
            return stated;

        var family = JsonElementReader.TryGetObject(properties, "annotation") is { } annotation
            ? JsonElementReader.TryGetString(annotation, "family")
            : null;

        foreach (var known in (ReadOnlySpan<string>)[OpenApiConnectionWebhook, OpenApiConnectionNotification, OpenApiConnection])
        {
            if (string.Equals(family, known, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return WebhookOperationNameRegex.IsMatch(operationId)
            ? OpenApiConnectionWebhook
            : OpenApiConnection;
    }

    internal static bool IsDeprecated(JsonElement properties)
        => (properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("isDeprecated", out var flag) && flag.ValueKind == JsonValueKind.True)
            || string.Equals(JsonElementReader.TryGetString(properties, "status"), "Deprecated", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flattens a sub-schema returned by a dynamic lookup into parameters named
    /// under <paramref name="prefix"/> (<c>body/recipient</c>).
    /// </summary>
    /// <param name="includeReadOnly">True for outputs, where read-only fields (createdon) are what is read.</param>
    public static IReadOnlyList<OperationParameter> ReadSubSchema(JsonElement schema, string prefix, bool required, bool includeReadOnly = false)
    {
        var parameters = new List<OperationParameter>();
        ReadObject(schema, prefix, required, parameters, depth: 0, includeReadOnly);
        return parameters;
    }

    private static void ReadObject(
        JsonElement schema,
        string? prefix,
        bool ancestorsRequired,
        List<OperationParameter> parameters,
        int depth,
        bool includeReadOnly = false)
    {
        // The payload uses "properties" for JSON-schema-shaped inputs and
        // "parameters" for the flattened form; both appear in the wild.
        var bag = JsonElementReader.TryGetObject(schema, "properties") ?? JsonElementReader.TryGetObject(schema, "parameters");
        if (bag is not { } members)
            return;

        var requiredNames = ReadRequiredNames(schema);

        foreach (var member in members.EnumerateObject())
        {
            var spec = member.Value;
            if (spec.ValueKind != JsonValueKind.Object || (!includeReadOnly && IsReadOnly(spec)))
                continue;

            var name = prefix is null ? member.Name : $"{prefix}/{member.Name}";
            var required = ancestorsRequired
                && (requiredNames.Contains(member.Name)
                    || (spec.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True));

            if (!HasDynamicSchema(spec) && JsonElementReader.TryGetObject(spec, "properties") is not null && depth < MaxFlattenDepth)
            {
                var before = parameters.Count;
                ReadObject(spec, name, required, parameters, depth + 1, includeReadOnly);
                if (parameters.Count > before)
                    continue;
            }

            parameters.Add(ReadParameter(name, spec, required));
        }
    }

    private static HashSet<string> ReadRequiredNames(JsonElement schema)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name)
                    required.Add(name);
            }
        }

        return required;
    }

    private static OperationParameter ReadParameter(string name, JsonElement spec, bool required)
        => new(
            Name: name,
            Type: JsonElementReader.TryGetString(spec, "type"),
            Required: required,
            Description: JsonElementReader.TryGetString(spec, "x-ms-summary")
                ?? JsonElementReader.TryGetString(spec, "title")
                ?? JsonElementReader.TryGetString(spec, "summary")
                ?? JsonElementReader.TryGetString(spec, "description"),
            AllowedValues: ReadAllowedValues(spec),
            DefaultValue: spec.TryGetProperty("default", out var value) ? ToScalarString(value) : null,
            // Two vocabularies are in play: connector Swagger uses
            // x-ms-dynamic-values/-schema, the operation API returns the newer
            // x-ms-dynamic-list/-properties for the same concepts.
            DynamicValues: ReadDynamicRef(spec, "x-ms-dynamic-values", "x-ms-dynamic-list"),
            DynamicTree: ReadDynamicRef(spec, "x-ms-dynamic-tree"),
            DynamicSchema: ReadDynamicRef(spec, "x-ms-dynamic-schema", "x-ms-dynamic-properties"));

    private static bool HasDynamicSchema(JsonElement spec)
        => spec.TryGetProperty("x-ms-dynamic-schema", out _) || spec.TryGetProperty("x-ms-dynamic-properties", out _);

    private static bool IsReadOnly(JsonElement spec)
        => spec.TryGetProperty("readOnly", out var flag) && flag.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Values are normalised to strings so a caller can compare a literal
    /// against them whatever type the connector declared them as.
    /// </summary>
    private static IReadOnlyList<string>? ReadAllowedValues(JsonElement spec)
    {
        if (!spec.TryGetProperty("enum", out var values) || values.ValueKind != JsonValueKind.Array)
            return null;

        var result = values.EnumerateArray().Select(ToScalarString).OfType<string>().ToList();
        return result.Count == 0 ? null : result;
    }

    /// <summary>A string, number or boolean as text; null for anything else.</summary>
    internal static string? ToScalarString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

    private static DynamicValuesRef? ReadDynamicRef(JsonElement spec, params string[] extensionNames)
    {
        var extension = extensionNames.Select(n => JsonElementReader.TryGetObject(spec, n)).FirstOrDefault(e => e is not null);
        if (extension is not { } source)
            return null;

        // A tree extension nests its resolvers under open/browse; either one
        // identifies the operation that supplies values.
        var operationId = JsonElementReader.TryGetString(source, "operationId");
        if (operationId is null)
        {
            foreach (var nested in (ReadOnlySpan<string>)["open", "browse"])
            {
                if (JsonElementReader.TryGetObject(source, nested) is { } child && JsonElementReader.TryGetString(child, "operationId") is { } id)
                {
                    operationId = id;
                    source = child;
                    break;
                }
            }
        }

        if (operationId is null)
            return null;

        Dictionary<string, string?>? arguments = null;
        if (JsonElementReader.TryGetObject(source, "parameters") is { } bag)
        {
            arguments = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var entry in bag.EnumerateObject())
                arguments[entry.Name] = DescribeResolverArgument(entry.Value);
        }

        // The list and values vocabularies name the same three paths differently.
        return new DynamicValuesRef(
            operationId,
            arguments,
            ItemsPath: JsonElementReader.TryGetString(source, "itemsPath") ?? JsonElementReader.TryGetString(source, "value-collection"),
            ValuePath: JsonElementReader.TryGetString(source, "itemValuePath") ?? JsonElementReader.TryGetString(source, "value-path"),
            TitlePath: JsonElementReader.TryGetString(source, "itemTitlePath") ?? JsonElementReader.TryGetString(source, "value-title"));
    }

    /// <summary>
    /// Renders one resolver argument: a literal, or a reference to another
    /// parameter of the same operation (<see cref="ResolverArgument"/>), so the caller can
    /// see the resolution order (a Teams message body depends on poster and
    /// location).
    /// </summary>
    private static string? DescribeResolverArgument(JsonElement argument)
    {
        if (argument.ValueKind != JsonValueKind.Object)
            return ToScalarString(argument) ?? argument.GetRawText();

        // An optional input (e.g. $select for a row's fields) is passed when
        // chosen and left out otherwise.
        if (JsonElementReader.TryGetString(argument, "parameterReference") is { } reference)
            return ResolverArgument.Reference(reference,
                optional: argument.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.False);

        // The older x-ms-dynamic-values vocabulary names the source parameter "parameter".
        if (JsonElementReader.TryGetString(argument, "parameter") is { } legacy)
            return ResolverArgument.Reference(legacy);

        if (argument.TryGetProperty("value", out var literal))
            return ToScalarString(literal) ?? literal.GetRawText();

        return argument.GetRawText();
    }
}
