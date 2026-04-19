using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Processes subprocess stderr JSON log lines and forwards them as MCP log notifications
/// via the SDK's client-facing logger provider. Optionally emits progress notifications.
/// </summary>
internal sealed class McpLogForwarder : ISubprocessOutputHandler
{
    private readonly ILogger _mcpLogger;
    private readonly StringBuilder _stdoutBuffer = new();
    private readonly List<string> _errorMessages = new();
    private readonly Func<ProgressNotificationValue, Task>? _sendProgress;

    private int _lineCount;
    private long _lastProgressTicks;

    /// <summary>
    /// Minimum interval between progress notifications to avoid flooding the client.
    /// </summary>
    internal static readonly TimeSpan ProgressRateLimit = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The accumulated stdout content (tool result data).
    /// </summary>
    public string StdoutContent => _stdoutBuffer.ToString();

    /// <summary>
    /// The last error/critical log messages from the subprocess (for error reporting in tool results).
    /// </summary>
    public string LastErrors => _errorMessages.Count > 0
        ? string.Join(System.Environment.NewLine, _errorMessages)
        : string.Empty;

    /// <summary>
    /// Number of progress notifications actually sent (for testing).
    /// </summary>
    internal int ProgressNotificationsSent { get; private set; }

    /// <summary>
    /// Creates a forwarder that sends log notifications to the MCP client.
    /// </summary>
    /// <param name="mcpLogger">
    /// An ILogger obtained from McpServer.AsClientLoggerProvider().
    /// Log calls on this logger are sent as MCP notifications/message to the client.
    /// </param>
    /// <param name="server">Optional MCP server instance for sending progress notifications.</param>
    /// <param name="progressToken">Optional progress token from the client request.</param>
    public McpLogForwarder(ILogger mcpLogger, McpServer? server = null, ProgressToken? progressToken = null)
        : this(mcpLogger, CreateSendProgress(server, progressToken))
    {
    }

    /// <summary>
    /// Internal constructor for testing, accepts a custom progress sender delegate.
    /// </summary>
    internal McpLogForwarder(ILogger mcpLogger, Func<ProgressNotificationValue, Task>? sendProgress)
    {
        _mcpLogger = mcpLogger;
        _sendProgress = sendProgress;
    }

    public Task OnStdoutLineAsync(string line)
    {
        _stdoutBuffer.AppendLine(line);
        return Task.CompletedTask;
    }

    public async Task OnStderrLineAsync(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        string? redactedMessage = null;
        var logLine = JsonLogLine.TryDeserialize(line);
        if (logLine != null)
        {
            redactedMessage = ForwardStructuredLog(logLine);
        }
        else
        {
            // Graceful fallback: forward non-JSON stderr as a warning
            // Also capture as error since raw stderr typically indicates problems
            // (e.g. argument parsing failures from System.CommandLine)
            redactedMessage = LogRedactionFilter.Redact(line);
            _mcpLogger.Log(LogLevel.Warning, "{Message}", redactedMessage);
            _errorMessages.Add(redactedMessage);
        }

        _lineCount++;
        await TrySendProgressAsync(redactedMessage).ConfigureAwait(false);
    }

    public Task OnProcessExitedAsync(int exitCode)
    {
        if (exitCode != 0)
        {
            _mcpLogger.LogWarning("Subprocess exited with code {ExitCode}", exitCode);
        }

        return Task.CompletedTask;
    }

    private string ForwardStructuredLog(JsonLogLine logLine)
    {
        var level = ParseLogLevel(logLine.Level);
        string redactedMessage = LogRedactionFilter.Redact(logLine.Message);
        _mcpLogger.Log(level, "[{Category}] {Message}", logLine.Category, redactedMessage);

        if (level >= LogLevel.Error)
        {
            _errorMessages.Add(redactedMessage);
        }

        return redactedMessage;
    }

    private async Task TrySendProgressAsync(string? message)
    {
        if (_sendProgress is null)
            return;

        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastProgressTicks, now);
        if (_lastProgressTicks != 0 && elapsed < ProgressRateLimit)
            return;

        _lastProgressTicks = now;
        ProgressNotificationsSent++;

        await _sendProgress(new ProgressNotificationValue
        {
            Progress = _lineCount,
            Message = message
        }).ConfigureAwait(false);
    }

    private static Func<ProgressNotificationValue, Task>? CreateSendProgress(McpServer? server, ProgressToken? progressToken)
    {
        if (server is null || progressToken is not { } token)
            return null;

        return value => server.NotifyProgressAsync(token, value);
    }

    private static LogLevel ParseLogLevel(string level) => level switch
    {
        "Trace" => LogLevel.Trace,
        "Debug" => LogLevel.Debug,
        "Information" => LogLevel.Information,
        "Warning" => LogLevel.Warning,
        "Error" => LogLevel.Error,
        "Critical" => LogLevel.Critical,
        _ => LogLevel.Information
    };
}
