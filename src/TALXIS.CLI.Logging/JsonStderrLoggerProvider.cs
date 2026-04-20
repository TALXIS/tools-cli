using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Logger provider that creates <see cref="JsonStderrLogger"/> instances.
/// Writes structured JSON log lines to stderr for consumption by the MCP server.
/// </summary>
public sealed class JsonStderrLoggerProvider : ILoggerProvider
{
    private readonly TextWriter _stderr = Console.Error;

    public ILogger CreateLogger(string categoryName)
    {
        return new JsonStderrLogger(categoryName, _stderr);
    }

    public void Dispose()
    {
        // Nothing to dispose — Console.Error is managed by the runtime
    }
}
