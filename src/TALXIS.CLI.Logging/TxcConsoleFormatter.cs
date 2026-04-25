using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Custom console formatter for human-friendly CLI output.
/// <list type="bullet">
///   <item>Bracketed level prefixes with ANSI color: <c>[INFO]</c>, <c>[WARN]</c>, <c>[ERROR]</c></item>
///   <item>HH:mm:ss timestamps</item>
///   <item>No category names (users don't care about <c>EntityListCliCommand[0]</c>)</item>
///   <item>Exception messages only — stack traces are suppressed unless verbose/debug</item>
///   <item>Plain ASCII — no emojis or unicode symbols (CONTRIBUTING.md)</item>
/// </list>
/// </summary>
internal sealed class TxcConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "txc";

    private readonly bool _verbose;

    public TxcConsoleFormatter(IOptions<TxcConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _verbose = options.Value.Verbose;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        // Redact before writing
        message = LogRedactionFilter.Redact(message) ?? string.Empty;

        // Timestamp
        textWriter.Write(DateTime.Now.ToString("HH:mm:ss"));
        textWriter.Write(' ');

        // Level prefix with ANSI color
        WriteLevel(textWriter, logEntry.LogLevel);

        // Message
        textWriter.Write(' ');
        textWriter.WriteLine(message);

        // Exception handling: only show message by default, full trace in verbose
        if (logEntry.Exception is not null)
        {
            if (_verbose)
            {
                // Full stack trace, indented
                var exceptionText = LogRedactionFilter.Redact(logEntry.Exception.ToString()) ?? string.Empty;
                foreach (var line in exceptionText.Split('\n'))
                {
                    textWriter.Write("         ");
                    textWriter.WriteLine(line.TrimEnd('\r'));
                }
            }
            else
            {
                // Walk the inner exception chain — show each message on its own line
                var inner = logEntry.Exception.InnerException;
                while (inner is not null)
                {
                    var innerMsg = LogRedactionFilter.Redact(inner.Message) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(innerMsg) && !message.Contains(innerMsg))
                    {
                        textWriter.Write("         ");
                        textWriter.WriteLine(innerMsg);
                    }
                    inner = inner.InnerException;
                }
            }
        }
    }

    private static void WriteLevel(TextWriter writer, LogLevel level)
    {
        // ANSI color codes + bracketed labels
        var (color, label) = level switch
        {
            LogLevel.Trace       => ("\x1b[90m", "[TRACE]"),    // dim gray
            LogLevel.Debug       => ("\x1b[90m", "[DEBUG]"),    // dim gray
            LogLevel.Information => ("\x1b[36m", "[INFO] "),    // cyan
            LogLevel.Warning     => ("\x1b[33m", "[WARN] "),    // yellow
            LogLevel.Error       => ("\x1b[31m", "[ERROR]"),    // red
            LogLevel.Critical    => ("\x1b[91m", "[CRIT] "),    // bright red
            _                    => ("",         "[?]    "),
        };

        writer.Write(color);
        writer.Write(label);
        writer.Write("\x1b[0m"); // reset
    }
}

/// <summary>
/// Options for <see cref="TxcConsoleFormatter"/>.
/// </summary>
internal sealed class TxcConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>
    /// When true, show full exception stack traces. When false (default),
    /// only show exception messages.
    /// </summary>
    public bool Verbose { get; set; }
}
