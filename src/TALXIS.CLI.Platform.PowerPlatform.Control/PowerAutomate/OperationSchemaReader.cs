using System.Text.Json;
using System.Text.RegularExpressions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Reads the authoritative parameter specification for one connector operation
/// from an <c>apiOperations/{operation}</c> payload, and infers the action type
/// a flow definition must declare for it.
/// </summary>
internal static partial class OperationSchemaReader
{
    public const string OpenApiConnection = "OpenApiConnection";
    public const string OpenApiConnectionWebhook = "OpenApiConnectionWebhook";
    public const string OpenApiConnectionNotification = "OpenApiConnectionNotification";

    /// <summary>
    /// Operations whose name follows this pattern are long-running webhook
    /// subscriptions even when the payload carries no annotation to say so.
    /// </summary>
    [GeneratedRegex("^(StartAndWait|WaitFor|Subscribe)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebhookOperationNameRegex { get; }

    public static OperationDetail Read(string connector, string operationId, JsonElement payload)
    {
        var properties = SwaggerOperationIndexer.TryGetObject(payload, "properties") ?? payload;
        var inputs = SwaggerOperationIndexer.TryGetObject(properties, "inputsDefinition");

        var parameters = ReadParameters(inputs);

        JsonElement? responseSchema = null;
        if (SwaggerOperationIndexer.TryGetObject(properties, "responsesDefinition") is { } responses)
        {
            // Clone: the owning JsonDocument is disposed by the caller.
            responseSchema = responses.Clone();
        }

        return new OperationDetail(
            Connector: connector,
            ApiId: PowerAutomateEndpointProvider.BuildApiId(connector),
            OperationId: operationId,
            Summary: SwaggerOperationIndexer.TryGetString(properties, "summary"),
            Description: SwaggerOperationIndexer.TryGetString(properties, "description"),
            ActionType: InferActionType(properties, operationId),
            IsDeprecated: IsDeprecated(properties),
            Parameters: parameters,
            ResponseSchema: responseSchema);
    }

    /// <summary>
    /// Determines the <c>type</c> a flow action must declare. Getting this
    /// wrong is a common and confusing failure — an approval declared as a
    /// plain connection silently never resumes — so the annotation is
    /// preferred and the name pattern is only a fallback.
    /// </summary>
    public static string InferActionType(JsonElement properties, string operationId)
    {
        var annotation = SwaggerOperationIndexer.TryGetObject(properties, "annotation");
        var family = annotation is { } a ? SwaggerOperationIndexer.TryGetString(a, "family") : null;

        // An explicit family is authoritative, including when it says this is a
        // plain connection despite a webhook-sounding name.
        if (string.Equals(family, OpenApiConnectionWebhook, StringComparison.OrdinalIgnoreCase))
            return OpenApiConnectionWebhook;
        if (string.Equals(family, OpenApiConnectionNotification, StringComparison.OrdinalIgnoreCase))
            return OpenApiConnectionNotification;
        if (string.Equals(family, OpenApiConnection, StringComparison.OrdinalIgnoreCase))
            return OpenApiConnection;

        return WebhookOperationNameRegex.IsMatch(operationId)
            ? OpenApiConnectionWebhook
            : OpenApiConnection;
    }

    private static bool IsDeprecated(JsonElement properties)
    {
        if (properties.TryGetProperty("isDeprecated", out var flag) && flag.ValueKind == JsonValueKind.True)
            return true;

        return string.Equals(
            SwaggerOperationIndexer.TryGetString(properties, "status"),
            "Deprecated",
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<OperationParameter> ReadParameters(JsonElement? inputs)
    {
        if (inputs is not { } definition)
            return [];

        // The payload uses "properties" for JSON-schema-shaped inputs and
        // "parameters" for the flattened form; both appear in the wild.
        var bag = SwaggerOperationIndexer.TryGetObject(definition, "properties")
            ?? SwaggerOperationIndexer.TryGetObject(definition, "parameters");

        if (bag is not { } parameters)
            return [];

        var required = ReadRequiredNames(definition);
        var result = new List<OperationParameter>();

        foreach (var entry in parameters.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
                continue;

            result.Add(ReadParameter(entry.Name, entry.Value, required.Contains(entry.Name)));
        }

        return result;
    }

    private static HashSet<string> ReadRequiredNames(JsonElement definition)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);

        if (definition.TryGetProperty("required", out var requiredArray)
            && requiredArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name)
                    required.Add(name);
            }
        }

        return required;
    }

    private static OperationParameter ReadParameter(string name, JsonElement spec, bool requiredByParent)
    {
        var requiredHere = spec.TryGetProperty("required", out var requiredFlag)
            && requiredFlag.ValueKind == JsonValueKind.True;

        return new OperationParameter(
            Name: name,
            Type: SwaggerOperationIndexer.TryGetString(spec, "type"),
            Required: requiredByParent || requiredHere,
            Description: SwaggerOperationIndexer.TryGetString(spec, "x-ms-summary")
                ?? SwaggerOperationIndexer.TryGetString(spec, "title")
                ?? SwaggerOperationIndexer.TryGetString(spec, "summary")
                ?? SwaggerOperationIndexer.TryGetString(spec, "description"),
            AllowedValues: ReadAllowedValues(spec),
            DefaultValue: ReadScalar(spec, "default"),
            Visibility: SwaggerOperationIndexer.TryGetString(spec, "x-ms-visibility"),
            // Two vocabularies are in play: the connector's Swagger uses
            // x-ms-dynamic-values/-schema, while the operation API returns the
            // newer x-ms-dynamic-list/-properties for the same concepts.
            DynamicValues: ReadDynamicRef(spec, "x-ms-dynamic-values", "x-ms-dynamic-list"),
            DynamicTree: ReadDynamicRef(spec, "x-ms-dynamic-tree"),
            DynamicSchema: ReadDynamicRef(spec, "x-ms-dynamic-schema", "x-ms-dynamic-properties"));
    }

    /// <summary>
    /// Reads an enum constraint. Values are normalised to strings so a caller
    /// can compare a definition's literal against them without caring whether
    /// the connector declared them as strings, numbers or booleans.
    /// </summary>
    private static IReadOnlyList<string>? ReadAllowedValues(JsonElement spec)
    {
        if (!spec.TryGetProperty("enum", out var values) || values.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            var scalar = ToScalarString(value);
            if (scalar is not null)
                result.Add(scalar);
        }

        return result.Count == 0 ? null : result;
    }

    private static string? ReadScalar(JsonElement spec, string name)
        => spec.TryGetProperty(name, out var value) ? ToScalarString(value) : null;

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
        JsonElement? found = null;
        foreach (var extensionName in extensionNames)
        {
            found = SwaggerOperationIndexer.TryGetObject(spec, extensionName);
            if (found is not null)
                break;
        }

        if (found is not { } extension)
            return null;

        // A tree extension nests its resolvers under open/browse; either one
        // identifies the operation that supplies values.
        var operationId = SwaggerOperationIndexer.TryGetString(extension, "operationId");
        var source = extension;

        if (operationId is null)
        {
            foreach (var nested in (ReadOnlySpan<string>)["open", "browse"])
            {
                if (SwaggerOperationIndexer.TryGetObject(extension, nested) is { } child
                    && SwaggerOperationIndexer.TryGetString(child, "operationId") is { } childOperation)
                {
                    operationId = childOperation;
                    source = child;
                    break;
                }
            }
        }

        if (operationId is null)
            return null;

        Dictionary<string, string?>? parameters = null;
        if (SwaggerOperationIndexer.TryGetObject(source, "parameters") is { } parameterBag)
        {
            parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var entry in parameterBag.EnumerateObject())
                parameters[entry.Name] = DescribeResolverArgument(entry.Value);
        }

        return new DynamicValuesRef(operationId, parameters);
    }

    /// <summary>
    /// Renders one argument of a dynamic resolver. An argument is either a
    /// literal, or a reference to another parameter of the same operation —
    /// rendered as <c>$ref:name</c> so a caller can see the resolution order
    /// (for Teams, the message body schema depends on poster and location).
    /// </summary>
    private static string? DescribeResolverArgument(JsonElement argument)
    {
        if (argument.ValueKind != JsonValueKind.Object)
            return ToScalarString(argument) ?? argument.GetRawText();

        if (SwaggerOperationIndexer.TryGetString(argument, "parameterReference") is { } reference)
            return "$ref:" + reference;

        if (argument.TryGetProperty("value", out var literal))
            return ToScalarString(literal) ?? literal.GetRawText();

        return argument.GetRawText();
    }
}
