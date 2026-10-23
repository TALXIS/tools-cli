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
        var properties = JsonRead.Object(payload, "properties") ?? payload;
        var inputs = JsonRead.Object(properties, "inputsDefinition");

        var parameters = new List<OperationParameter>();
        if (inputs is { } definition)
            ReadObject(definition, prefix: null, ancestorsRequired: true, parameters, depth: 0);

        return new OperationDetail(
            Connector: connector,
            ApiId: PowerAutomateEndpointProvider.BuildApiId(connector),
            OperationId: operationId,
            Summary: JsonRead.String(properties, "summary"),
            Description: JsonRead.String(properties, "description"),
            ActionType: InferActionType(properties, operationId),
            IsDeprecated: IsDeprecated(properties),
            Parameters: parameters,
            // Clone: the owning JsonDocument is disposed by the caller.
            ResponseSchema: JsonRead.Object(properties, "responsesDefinition")?.Clone(),
            IsTrigger: JsonRead.String(properties, "trigger") is not null);
    }

    /// <summary>
    /// Determines the <c>type</c> a flow action must declare. An approval
    /// declared as a plain connection silently never resumes, so the
    /// annotation is preferred and the name pattern is only a fallback.
    /// </summary>
    public static string InferActionType(JsonElement properties, string operationId)
    {
        // The operation API states the type outright; the rest is for payloads without it.
        if (JsonRead.String(properties, "operationType") is { Length: > 0 } stated)
            return stated;

        var family = JsonRead.Object(properties, "annotation") is { } annotation
            ? JsonRead.String(annotation, "family")
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

    private static bool IsDeprecated(JsonElement properties)
        => (properties.TryGetProperty("isDeprecated", out var flag) && flag.ValueKind == JsonValueKind.True)
            || string.Equals(JsonRead.String(properties, "status"), "Deprecated", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flattens a sub-schema returned by a dynamic lookup into parameters named
    /// under <paramref name="prefix"/> (<c>body/recipient</c>).
    /// </summary>
    public static IReadOnlyList<OperationParameter> ReadSubSchema(JsonElement schema, string prefix, bool required)
    {
        var parameters = new List<OperationParameter>();
        ReadObject(schema, prefix, required, parameters, depth: 0);
        return parameters;
    }

    private static void ReadObject(
        JsonElement schema,
        string? prefix,
        bool ancestorsRequired,
        List<OperationParameter> parameters,
        int depth)
    {
        // The payload uses "properties" for JSON-schema-shaped inputs and
        // "parameters" for the flattened form; both appear in the wild.
        var bag = JsonRead.Object(schema, "properties") ?? JsonRead.Object(schema, "parameters");
        if (bag is not { } members)
            return;

        var requiredNames = ReadRequiredNames(schema);

        foreach (var member in members.EnumerateObject())
        {
            var spec = member.Value;
            if (spec.ValueKind != JsonValueKind.Object || IsReadOnly(spec))
                continue;

            var name = prefix is null ? member.Name : $"{prefix}/{member.Name}";
            var required = ancestorsRequired
                && (requiredNames.Contains(member.Name)
                    || (spec.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True));

            if (!HasDynamicSchema(spec) && JsonRead.Object(spec, "properties") is not null && depth < MaxFlattenDepth)
            {
                var before = parameters.Count;
                ReadObject(spec, name, required, parameters, depth + 1);
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
            Type: JsonRead.String(spec, "type"),
            Required: required,
            Description: JsonRead.String(spec, "x-ms-summary")
                ?? JsonRead.String(spec, "title")
                ?? JsonRead.String(spec, "summary")
                ?? JsonRead.String(spec, "description"),
            AllowedValues: ReadAllowedValues(spec),
            DefaultValue: spec.TryGetProperty("default", out var value) ? ToScalarString(value) : null,
            Visibility: JsonRead.String(spec, "x-ms-visibility"),
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

    private static string? ToScalarString(JsonElement value)
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
        var extension = extensionNames.Select(n => JsonRead.Object(spec, n)).FirstOrDefault(e => e is not null);
        if (extension is not { } source)
            return null;

        // A tree extension nests its resolvers under open/browse; either one
        // identifies the operation that supplies values.
        var operationId = JsonRead.String(source, "operationId");
        if (operationId is null)
        {
            foreach (var nested in (ReadOnlySpan<string>)["open", "browse"])
            {
                if (JsonRead.Object(source, nested) is { } child && JsonRead.String(child, "operationId") is { } id)
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
        if (JsonRead.Object(source, "parameters") is { } bag)
        {
            arguments = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var entry in bag.EnumerateObject())
                arguments[entry.Name] = DescribeResolverArgument(entry.Value);
        }

        // The list and values vocabularies name the same three paths differently.
        return new DynamicValuesRef(
            operationId,
            arguments,
            ItemsPath: JsonRead.String(source, "itemsPath") ?? JsonRead.String(source, "value-collection"),
            ValuePath: JsonRead.String(source, "itemValuePath") ?? JsonRead.String(source, "value-path"),
            TitlePath: JsonRead.String(source, "itemTitlePath") ?? JsonRead.String(source, "value-title"));
    }

    /// <summary>
    /// Renders one resolver argument: a literal, or a reference to another
    /// parameter of the same operation as <c>$ref:name</c>, so the caller can
    /// see the resolution order (a Teams message body depends on poster and
    /// location).
    /// </summary>
    private static string? DescribeResolverArgument(JsonElement argument)
    {
        if (argument.ValueKind != JsonValueKind.Object)
            return ToScalarString(argument) ?? argument.GetRawText();

        if (JsonRead.String(argument, "parameterReference") is { } reference)
            return "$ref:" + reference;

        if (argument.TryGetProperty("value", out var literal))
            return ToScalarString(literal) ?? literal.GetRawText();

        return argument.GetRawText();
    }
}
