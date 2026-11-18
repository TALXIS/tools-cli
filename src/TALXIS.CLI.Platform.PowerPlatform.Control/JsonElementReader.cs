using System.Text.Json;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Tolerant reads of JSON the platform returns: a property of the wrong kind,
/// or a parent that is not an object, reads as absent.
/// </summary>
internal static class JsonElementReader
{
    public static string? TryGetString(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    public static JsonElement? TryGetObject(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : null;
}
