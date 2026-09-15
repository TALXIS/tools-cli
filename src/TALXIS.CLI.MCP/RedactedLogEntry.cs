using System.Text.Json.Serialization;

namespace TALXIS.CLI.MCP;

/// <summary>
/// A single redacted log entry captured from a CLI subprocess stderr stream.
/// This is the MCP-internal representation — all values are already redacted
/// via <see cref="TALXIS.CLI.Logging.LogRedactionFilter"/> before storage.
/// </summary>
internal sealed record RedactedLogEntry(
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object?>? Data = null);
