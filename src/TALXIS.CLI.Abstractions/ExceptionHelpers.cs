namespace TALXIS.CLI.Abstractions;

/// <summary>
/// Shared exception utilities. Extracted from <c>TxcLeafCommand</c> (Core) and
/// <c>McpToolResultFactory</c> (MCP) to eliminate duplication.
/// </summary>
public static class ExceptionHelpers
{
    /// <summary>
    /// Walks the <see cref="Exception.InnerException"/> chain and returns the
    /// deepest (innermost) exception. This is the one that usually contains the
    /// actionable root-cause message.
    /// </summary>
    public static Exception GetInnermostException(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex;
    }
}
