namespace TALXIS.CLI.Core;

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
    // Keep the redirect target scoped to the current async flow so concurrent in-process
    // tool executions do not overwrite each other's output destination.
    private static readonly AsyncLocal<TextWriter?> _writer = new();

    private static TextWriter CurrentWriter => _writer.Value ?? Console.Out;

    /// <summary>
    /// Replaces the output target. Returns a scope that restores the previous writer on dispose.
    /// Used by the MCP server to capture in-process tool output.
    /// </summary>
    public static IDisposable RedirectTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var previous = _writer.Value;
        _writer.Value = writer;
        return new RedirectScope(previous);
    }

    public static void WriteLine(string message) => CurrentWriter.WriteLine(message);

    public static void WriteLine() => CurrentWriter.WriteLine();

    public static void Write(string message) => CurrentWriter.Write(message);

    private sealed class RedirectScope : IDisposable
    {
        private readonly TextWriter? _previous;
        public RedirectScope(TextWriter? previous) => _previous = previous;
        public void Dispose() => _writer.Value = _previous;
    }
}
