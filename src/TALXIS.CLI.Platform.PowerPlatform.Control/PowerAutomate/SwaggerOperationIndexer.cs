using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Turns a connector payload's embedded Swagger document into a flat operation
/// index. Only what is needed to choose an operation is extracted; the full
/// parameter spec comes from <see cref="OperationSchemaReader"/>.
/// </summary>
internal static class SwaggerOperationIndexer
{
    private static readonly string[] HttpMethods =
        ["get", "put", "post", "delete", "patch", "head", "options"];

    /// <summary>
    /// Builds the index from a <c>/powerautomate/apis/{connector}?$expand=swagger</c>
    /// payload, optionally keeping only operations matching <paramref name="query"/>.
    /// </summary>
    public static ConnectorDetail Index(JsonElement connectorPayload, string fallbackName, string? query)
    {
        var name = TryGetString(connectorPayload, "name") ?? fallbackName;
        var properties = TryGetObject(connectorPayload, "properties");
        var displayName = properties is { } p ? TryGetString(p, "displayName") : null;

        var operations = new List<ConnectorOperationSummary>();

        if (properties is { } props
            && TryGetObject(props, "swagger") is { } swagger
            && TryGetObject(swagger, "paths") is { } paths)
        {
            foreach (var pathEntry in paths.EnumerateObject())
            {
                if (pathEntry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var methodEntry in pathEntry.Value.EnumerateObject())
                {
                    if (!IsHttpMethod(methodEntry.Name) || methodEntry.Value.ValueKind != JsonValueKind.Object)
                        continue;

                    var operation = methodEntry.Value;
                    var operationId = TryGetString(operation, "operationId");
                    if (string.IsNullOrWhiteSpace(operationId))
                        continue;

                    operations.Add(new ConnectorOperationSummary(
                        OperationId: operationId,
                        Summary: TryGetString(operation, "summary") ?? TryGetString(operation, "description"),
                        Method: methodEntry.Name.ToUpperInvariant(),
                        Path: pathEntry.Name,
                        ParamCount: CountParameters(operation),
                        IsDeprecated: IsDeprecated(operation),
                        IsTrigger: operation.TryGetProperty("x-ms-trigger", out _)));
                }
            }
        }

        operations.Sort(static (a, b) => string.CompareOrdinal(a.OperationId, b.OperationId));

        var total = operations.Count;
        IReadOnlyList<ConnectorOperationSummary> result = operations;
        int? matched = null;

        if (!string.IsNullOrWhiteSpace(query))
        {
            result = operations.Where(o => Matches(o, query)).ToList();
            matched = result.Count;
        }

        return new ConnectorDetail(
            Name: name,
            DisplayName: displayName,
            ApiId: PowerAutomateEndpointProvider.BuildApiId(name),
            OperationCount: total,
            MatchedOperations: matched,
            Operations: result);
    }

    private static bool Matches(ConnectorOperationSummary operation, string query)
        => operation.OperationId.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (operation.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsHttpMethod(string name)
        => HttpMethods.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An operation counts as deprecated when Swagger marks it so, or when the
    /// connector's own annotation reports that status.
    /// </summary>
    private static bool IsDeprecated(JsonElement operation)
    {
        if (operation.TryGetProperty("deprecated", out var deprecated)
            && deprecated.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return TryGetObject(operation, "x-ms-api-annotation") is { } annotation
            && string.Equals(TryGetString(annotation, "status"), "Deprecated", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Counts inputs the way an author perceives them: a body parameter is
    /// counted by its expanded properties, since those are the fields actually
    /// filled in, not the single wrapper.
    /// </summary>
    private static int CountParameters(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.ValueKind != JsonValueKind.Object)
                continue;

            var isBody = string.Equals(TryGetString(parameter, "in"), "body", StringComparison.OrdinalIgnoreCase);
            if (isBody
                && TryGetObject(parameter, "schema") is { } schema
                && TryGetObject(schema, "properties") is { } bodyProperties)
            {
                count += bodyProperties.EnumerateObject().Count();
                continue;
            }

            count++;
        }

        return count;
    }

    internal static JsonElement? TryGetObject(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : null;

    internal static string? TryGetString(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}
