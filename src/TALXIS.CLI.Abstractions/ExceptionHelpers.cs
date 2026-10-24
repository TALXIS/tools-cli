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

    /// <summary>
    /// Returns the first exception of type <typeparamref name="T"/> in the
    /// inner-exception chain (including <paramref name="ex"/>), or null.
    /// </summary>
    public static T? FindInChain<T>(Exception ex) where T : Exception
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is T match) return match;
        }
        return null;
    }
}
