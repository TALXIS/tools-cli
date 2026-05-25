namespace TALXIS.CLI.Abstractions;

/// <summary>
/// Shared constants used across CLI projects. Lives in Abstractions so both
/// Core and Logging can reference them without a circular dependency.
/// </summary>
public static class TxcConstants
{
    /// <summary>
    /// GitHub repository issues URL for support escalation messages.
    /// Used by <c>TxcSupportInfo</c> (Logging), <c>OutputFormatter</c> (Core),
    /// and <c>McpToolResultFactory</c> (MCP) to produce consistent error output.
    /// </summary>
    public const string RepositoryIssuesUrl = "https://github.com/TALXIS/tools-cli/issues";
}
