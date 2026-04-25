namespace TALXIS.CLI.Core;

/// <summary>
/// Ambient storage for the active output format, scoped per async flow.
/// Set once during command dispatch (from the global <c>--format</c> flag
/// or TTY auto-detection) and read by <see cref="OutputFormatter"/>.
/// </summary>
public static class OutputContext
{
    private static readonly AsyncLocal<OutputFormat?> _format = new();

    /// <summary>
    /// The effective output format for the current invocation.
    /// Falls back to TTY auto-detection when no explicit format is set:
    /// JSON when stdout is redirected (pipes, MCP), text for interactive terminals.
    /// </summary>
    public static OutputFormat Format
    {
        get => _format.Value ?? (Console.IsOutputRedirected ? OutputFormat.Json : OutputFormat.Text);
        set => _format.Value = value;
    }

    /// <summary>Whether the active format is JSON.</summary>
    public static bool IsJson => Format == OutputFormat.Json;
}
