using System.Text.Json;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>Null-tolerant property accessors for the loosely shaped metadata payloads.</summary>
internal static class JsonRead
{
    public static JsonElement? Object(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : null;

    public static string? String(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    /// <summary>Items of a bare array, or of the <c>value</c> array of an OData envelope.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : [];
    }
}
