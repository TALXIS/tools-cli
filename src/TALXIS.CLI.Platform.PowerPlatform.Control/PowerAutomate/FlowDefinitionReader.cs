using System.Text.Json;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>A flow definition plus the connection references that bind it.</summary>
internal sealed record FlowDocument(JsonElement Definition, JsonElement? ConnectionReferences);

/// <summary>
/// Locates the workflow definition inside whichever envelope a locally
/// scaffolded flow happens to use.
/// </summary>
/// <remarks>
/// A flow reaches this tool in one of three shapes: the bare Logic Apps
/// definition, the Dataverse client-data envelope that wraps it under
/// <c>properties.definition</c>, or that envelope serialized into a string
/// (how the workflow record stores it). Rather than guess from the file name,
/// each shape is probed in turn, so the rule engine downstream always sees a
/// plain definition.
/// </remarks>
internal static class FlowDefinitionReader
{
    /// <summary>How many nested string envelopes to unwrap before giving up.</summary>
    private const int MaxUnwrapDepth = 3;

    public static bool TryRead(JsonElement document, out FlowDocument flow, out string? error)
    {
        flow = null!;
        error = null;

        var current = document;

        for (var depth = 0; depth <= MaxUnwrapDepth; depth++)
        {
            if (current.ValueKind == JsonValueKind.String)
            {
                // A client-data payload stored as an escaped JSON string.
                var raw = current.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    break;

                try
                {
                    using var nested = JsonDocument.Parse(raw);
                    current = nested.RootElement.Clone();
                    continue;
                }
                catch (JsonException)
                {
                    break;
                }
            }

            if (current.ValueKind != JsonValueKind.Object)
                break;

            // The client-data envelope: definition and bindings side by side.
            if (SwaggerOperationIndexer.TryGetObject(current, "properties") is { } properties)
            {
                if (SwaggerOperationIndexer.TryGetObject(properties, "definition") is { } wrapped)
                {
                    flow = new FlowDocument(
                        wrapped,
                        SwaggerOperationIndexer.TryGetObject(properties, "connectionReferences"));
                    return true;
                }
            }

            // A bare definition, or one alongside sibling connection references.
            if (LooksLikeDefinition(current))
            {
                flow = new FlowDocument(
                    current,
                    SwaggerOperationIndexer.TryGetObject(current, "connectionReferences"));
                return true;
            }

            // Some templates nest the client data one level down under a
            // property whose name varies by casing.
            if (TryGetPropertyIgnoreCase(current, "clientdata", out var clientData))
            {
                current = clientData;
                continue;
            }

            if (SwaggerOperationIndexer.TryGetObject(current, "definition") is { } definition)
            {
                flow = new FlowDocument(
                    definition,
                    SwaggerOperationIndexer.TryGetObject(current, "connectionReferences"));
                return true;
            }

            break;
        }

        error = "Could not locate a flow definition. Expected a Logic Apps definition with 'triggers'/'actions', "
            + "a client-data envelope with 'properties.definition', or that envelope as an escaped JSON string.";
        return false;
    }

    private static bool LooksLikeDefinition(JsonElement element)
        => element.TryGetProperty("triggers", out _)
            || element.TryGetProperty("actions", out _)
            || element.TryGetProperty("$schema", out _);

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
