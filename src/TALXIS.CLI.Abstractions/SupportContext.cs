using System.Text.Json.Serialization;

namespace TALXIS.CLI.Abstractions;

/// <summary>
/// Troubleshooting context included in error output across all surfaces
/// (CLI stdout JSON, MCP structuredContent, stderr text, App Insights).
/// Shared model so all three assembly paths (Core, Logging, MCP) produce
/// consistent support escalation data.
/// </summary>
public sealed class SupportContext
{
    public string? SessionId { get; init; }
    public string? OperationId { get; init; }
    public string ReportUrl { get; init; } = TxcConstants.RepositoryIssuesUrl;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogFilePath { get; init; }

    /// <summary>True when at least one identifying field is available.</summary>
    [JsonIgnore]
    public bool HasContext => SessionId is not null || OperationId is not null;

    /// <summary>
    /// Formats the support context as a human-readable text block for stderr output.
    /// Returns an empty string if no context is available (e.g. Debug builds).
    /// </summary>
    public string FormatAsText()
    {
        if (!HasContext)
            return string.Empty;

        var lines = $"If this is unexpected, report at {ReportUrl}{Environment.NewLine}" +
                    $"  Session: {SessionId ?? "unknown"}{Environment.NewLine}" +
                    $"  Operation: {OperationId ?? "unknown"}";

        if (LogFilePath is not null)
            lines += $"{Environment.NewLine}  Logs: {LogFilePath}";

        return lines;
    }
}
