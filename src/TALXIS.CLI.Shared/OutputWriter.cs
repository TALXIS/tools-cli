namespace TALXIS.CLI.Shared;

/// <summary>
/// Writes command result data to stdout. Use this for output that IS the tool result
/// (e.g., component listings, explanations, conversion paths).
/// 
/// For diagnostic messages (progress, status, warnings, errors), use ILogger instead.
/// 
/// In MCP mode: stdout is captured as the tool result returned to the client.
/// In terminal mode: stdout is displayed to the user.
/// </summary>
public static class OutputWriter
{
    private static TextWriter _writer = Console.Out;

    /// <summary>
    /// Replaces the output target. Returns a scope that restores the previous writer on dispose.
    /// Used by the MCP server to capture in-process tool output.
    /// </summary>
    public static IDisposable RedirectTo(TextWriter writer)
    {
        var previous = _writer;
        _writer = writer;
        return new RedirectScope(previous);
    }

    public static void WriteLine(string message) => _writer.WriteLine(message);

    public static void WriteLine() => _writer.WriteLine();

    public static void Write(string message) => _writer.Write(message);

    private sealed class RedirectScope : IDisposable
    {
        private readonly TextWriter _previous;
        public RedirectScope(TextWriter previous) => _previous = previous;
        public void Dispose() => _writer = _previous;
    }
}
