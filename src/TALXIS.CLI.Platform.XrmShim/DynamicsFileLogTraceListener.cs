using System.Diagnostics;
using System.Text;

namespace Microsoft.Xrm.Tooling.Connector;

/// <summary>
/// Shim for the legacy <c>DynamicsFileLogTraceListener</c> which originally
/// inherits from <c>Microsoft.VisualBasic.Logging.FileLogTraceListener</c> —
/// a type that does not exist on modern .NET.
///
/// CMT's <c>TraceLogger.GetLogFilePath()</c> enumerates trace listeners looking
/// for a <c>DynamicsFileLogTraceListener</c> and reads its <see cref="FullLogFileName"/>
/// property. Without this type, CMT throws a <c>TypeLoadException</c> at runtime.
///
/// This shim extends <see cref="TraceListener"/> directly and writes trace
/// output to a file, providing the <see cref="FullLogFileName"/> property
/// that CMT expects.
/// </summary>
public class DynamicsFileLogTraceListener : TraceListener
{
    private readonly string _logFilePath;
    private readonly object _writeLock = new();
    private StreamWriter? _writer;

    public DynamicsFileLogTraceListener()
        : this(GenerateDefaultLogFilePath())
    {
    }

    public DynamicsFileLogTraceListener(string logFilePath)
    {
        _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));

        string? directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Full path to the log file. Read by CMT's
    /// <c>TraceLogger.GetLogFilePath()</c>.
    /// </summary>
    public string FullLogFileName => _logFilePath;

    public override void Write(string? message)
    {
        if (message is null) return;

        lock (_writeLock)
        {
            EnsureWriter();
            _writer?.Write(message);
            _writer?.Flush();
        }
    }

    public override void WriteLine(string? message)
    {
        if (message is null) return;

        lock (_writeLock)
        {
            EnsureWriter();
            _writer?.WriteLine(message);
            _writer?.Flush();
        }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{eventType}] {message}");
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        string message = args is not null && format is not null
            ? string.Format(format, args)
            : format ?? string.Empty;
        TraceEvent(eventCache, source, eventType, id, message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_writeLock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        base.Dispose(disposing);
    }

    private void EnsureWriter()
    {
        _writer ??= new StreamWriter(_logFilePath, append: true, Encoding.UTF8);
    }

    private static string GenerateDefaultLogFilePath()
    {
        string logDir = Path.Combine(Path.GetTempPath(), "txc", "cmt-logs");
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, $"DynamicsFileLog-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
    }
}
