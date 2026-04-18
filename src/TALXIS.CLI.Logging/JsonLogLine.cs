using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Represents a single structured log line emitted to stderr in JSON format.
/// This is the wire contract between the CLI subprocess and the MCP server.
/// </summary>
public sealed class JsonLogLine
{
    [JsonPropertyName("ts")]
    public string Timestamp { get; set; } = default!;

    [JsonPropertyName("level")]
    public string Level { get; set; } = default!;

    [JsonPropertyName("cat")]
    public string Category { get; set; } = default!;

    [JsonPropertyName("msg")]
    public string Message { get; set; } = default!;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Data { get; set; }

    [JsonPropertyName("progress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Progress { get; set; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static JsonLogLine? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonLogLine>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
