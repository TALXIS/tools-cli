using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Central logging factory for the TALXIS CLI.
/// Selects the appropriate logger provider based on TXC_LOG_FORMAT env var:
/// - "json": Structured JSON to stderr (for MCP server consumption)
/// - otherwise: Colored console output (for human users)
/// </summary>
public static class TxcLoggerFactory
{
    private static readonly Lazy<ILoggerFactory> _factory = new(Create);

    public static ILoggerFactory Instance => _factory.Value;

    public static ILogger<T> CreateLogger<T>() => Instance.CreateLogger<T>();

    public static ILogger CreateLogger(string categoryName) => Instance.CreateLogger(categoryName);

    private static ILoggerFactory Create()
    {
        string? logFormat = System.Environment.GetEnvironmentVariable("TXC_LOG_FORMAT");
        string? configuredLogLevel = System.Environment.GetEnvironmentVariable("TXC_LOG_LEVEL");
        // Use JSON stderr mode when explicitly requested OR when stdout is
        // redirected (e.g. piped into an MCP stdio transport).  This prevents
        // the SimpleConsole provider from writing to stdout and corrupting the
        // JSON-RPC stream.
        bool jsonMode = logFormat == "json" || System.Console.IsOutputRedirected;

        LogLevel minimumLogLevel = Enum.TryParse<LogLevel>(configuredLogLevel, ignoreCase: true, out LogLevel parsed)
            ? parsed
            : LogLevel.Information;

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLogLevel);

            if (jsonMode)
            {
                builder.AddProvider(new JsonStderrLoggerProvider());
            }
            else
            {
                builder.AddSimpleConsole(opts =>
                {
                    opts.SingleLine = true;
                    opts.ColorBehavior = LoggerColorBehavior.Enabled;
                });
            }
        });
    }
}
