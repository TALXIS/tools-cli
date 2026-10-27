using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Turns a connector payload's embedded Swagger document into a flat operation
/// index. Only what is needed to choose an operation is extracted; the
/// parameter spec comes from <see cref="OperationSchemaReader"/>.
/// </summary>
internal static class SwaggerOperationIndexer
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch", "head", "options"];

    /// <summary>
    /// Builds the index from a <c>/powerautomate/apis/{connector}?$expand=swagger</c>
    /// payload, optionally keeping only operations matching <paramref name="search"/>.
    /// </summary>
    public static ConnectorDetail Index(JsonElement connectorPayload, string fallbackName, string? search)
    {
        var name = JsonElementReader.TryGetString(connectorPayload, "name") ?? fallbackName;
        var properties = JsonElementReader.TryGetObject(connectorPayload, "properties");
        var displayName = properties is { } p ? JsonElementReader.TryGetString(p, "displayName") : null;

        var operations = new List<ConnectorOperationSummary>();

        if (properties is { } props
            && JsonElementReader.TryGetObject(props, "swagger") is { } swagger
            && JsonElementReader.TryGetObject(swagger, "paths") is { } paths)
        {
            foreach (var pathEntry in paths.EnumerateObject())
            {
                if (pathEntry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var methodEntry in pathEntry.Value.EnumerateObject())
                {
                    if (!HttpMethods.Contains(methodEntry.Name, StringComparer.OrdinalIgnoreCase)
                        || methodEntry.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var operation = methodEntry.Value;
                    var operationId = JsonElementReader.TryGetString(operation, "operationId");
                    if (string.IsNullOrWhiteSpace(operationId) || IsInternal(operation))
                        continue;

                    operations.Add(new ConnectorOperationSummary(
                        OperationId: operationId,
                        Kind: ResolveKind(pathEntry.Value, operation),
                        Summary: JsonElementReader.TryGetString(operation, "summary") ?? JsonElementReader.TryGetString(operation, "description"),
                        Method: methodEntry.Name.ToUpperInvariant(),
                        Path: pathEntry.Name,
                        IsDeprecated: IsDeprecated(operation)));
                }
            }
        }

        operations.Sort(static (a, b) => string.CompareOrdinal(a.OperationId, b.OperationId));

        IReadOnlyList<ConnectorOperationSummary> result = operations;
        int? matched = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            result = operations
                .Where(o => o.OperationId.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (o.Summary?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            matched = result.Count;
        }

        return new ConnectorDetail(
            Name: name,
            DisplayName: displayName,
            ApiId: ConnectorIds.ToPath(name),
            OperationCount: operations.Count,
            MatchedOperations: matched,
            Operations: result);
    }

    /// <summary>
    /// A trigger is marked with <c>x-ms-trigger</c>; webhook triggers also
    /// declare the callback payload with <c>x-ms-notification-content</c>.
    /// </summary>
    private static string ResolveKind(JsonElement pathItem, JsonElement operation)
    {
        if (!operation.TryGetProperty("x-ms-trigger", out _))
            return ConnectorOperationKind.Action;

        return pathItem.TryGetProperty("x-ms-notification-content", out _)
            || operation.TryGetProperty("x-ms-notification-content", out _)
                ? ConnectorOperationKind.WebhookTrigger
                : ConnectorOperationKind.Trigger;
    }

    /// <summary>
    /// Internal operations are plumbing behind other operations (e.g. the
    /// Outlook <c>Create...PokeSubscription</c> calls a webhook trigger makes);
    /// the designer never offers them and a flow must not call them.
    /// </summary>
    private static bool IsInternal(JsonElement operation)
        => string.Equals(JsonElementReader.TryGetString(operation, "x-ms-visibility"), "internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeprecated(JsonElement operation)
        => (operation.TryGetProperty("deprecated", out var deprecated) && deprecated.ValueKind == JsonValueKind.True)
            || (JsonElementReader.TryGetObject(operation, "x-ms-api-annotation") is { } annotation
                && string.Equals(JsonElementReader.TryGetString(annotation, "status"), "Deprecated", StringComparison.OrdinalIgnoreCase));
}
