using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using TALXIS.CLI.Logging;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class McpLogForwarderTests
{
    [Fact]
    public async Task OnStdoutLine_BuffersContent()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        await forwarder.OnStdoutLineAsync("line1");
        await forwarder.OnStdoutLineAsync("line2");

        Assert.Contains("line1", forwarder.StdoutContent);
        Assert.Contains("line2", forwarder.StdoutContent);
    }

    [Fact]
    public async Task OnStderrLine_ValidJson_ForwardsStructured()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        var logLine = new JsonLogLine
        {
            Timestamp = "2024-01-15T10:30:45Z",
            Level = "Information",
            Category = "TestCat",
            Message = "Hello from subprocess"
        };

        await forwarder.OnStderrLineAsync(logLine.Serialize());

        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Information, logger.LogEntries[0].Level);
        Assert.Contains("Hello from subprocess", logger.LogEntries[0].Message);
    }

    [Fact]
    public async Task OnStderrLine_InvalidJson_ForwardsAsWarning()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        await forwarder.OnStderrLineAsync("This is plain text stderr output");

        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
        Assert.Contains("This is plain text stderr output", logger.LogEntries[0].Message);
    }

    [Fact]
    public async Task OnStderrLine_EmptyLine_Ignored()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        await forwarder.OnStderrLineAsync("");
        await forwarder.OnStderrLineAsync("   ");

        Assert.Empty(logger.LogEntries);
    }

    [Fact]
    public async Task OnProcessExited_NonZero_LogsWarning()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        await forwarder.OnProcessExitedAsync(1);

        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
        Assert.Contains("1", logger.LogEntries[0].Message);
    }

    [Fact]
    public async Task OnProcessExited_Zero_NoLog()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        await forwarder.OnProcessExitedAsync(0);

        Assert.Empty(logger.LogEntries);
    }

    [Fact]
    public async Task OnStderrLine_RedactsSecrets()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        var logLine = new JsonLogLine
        {
            Timestamp = "2024-01-15T10:30:45Z",
            Level = "Information",
            Category = "Auth",
            Message = "Connecting with Password=hunter2;Server=localhost"
        };

        await forwarder.OnStderrLineAsync(logLine.Serialize());

        Assert.Single(logger.LogEntries);
        Assert.DoesNotContain("hunter2", logger.LogEntries[0].Message);
        Assert.Contains("***REDACTED***", logger.LogEntries[0].Message);
    }

    [Fact]
    public async Task OnStderrLine_MapsAllLogLevels()
    {
        var testCases = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };
        var expectedLevels = new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical };

        for (int i = 0; i < testCases.Length; i++)
        {
            var logger = new CapturingLogger();
            var forwarder = new McpLogForwarder(logger);

            var logLine = new JsonLogLine
            {
                Timestamp = "2024-01-15T10:30:45Z",
                Level = testCases[i],
                Category = "Test",
                Message = "msg"
            };

            await forwarder.OnStderrLineAsync(logLine.Serialize());

            Assert.Single(logger.LogEntries);
            Assert.Equal(expectedLevels[i], logger.LogEntries[0].Level);
        }
    }

    [Fact]
    public async Task OnStderrLine_WithProgressToken_SendsProgressNotification()
    {
        var logger = new CapturingLogger();
        var progressValues = new List<ProgressNotificationValue>();
        Func<ProgressNotificationValue, Task> sendProgress = pv =>
        {
            progressValues.Add(pv);
            return Task.CompletedTask;
        };
        var forwarder = new McpLogForwarder(logger, sendProgress);

        await forwarder.OnStderrLineAsync("some stderr output");

        Assert.Single(progressValues);
        Assert.Equal(1, progressValues[0].Progress);
        Assert.Contains("some stderr output", progressValues[0].Message!);
        Assert.Equal(1, forwarder.ProgressNotificationsSent);
    }

    [Fact]
    public async Task OnStderrLine_WithoutProgressToken_NoProgressNotification()
    {
        var logger = new CapturingLogger();
        var forwarder = new McpLogForwarder(logger);

        await forwarder.OnStderrLineAsync("some stderr output");

        // Should not crash and should still log normally
        Assert.Single(logger.LogEntries);
        Assert.Equal(0, forwarder.ProgressNotificationsSent);
    }

    [Fact]
    public async Task Progress_RateLimited_NotEveryLine()
    {
        var logger = new CapturingLogger();
        var progressValues = new List<ProgressNotificationValue>();
        Func<ProgressNotificationValue, Task> sendProgress = pv =>
        {
            progressValues.Add(pv);
            return Task.CompletedTask;
        };
        var forwarder = new McpLogForwarder(logger, sendProgress);

        // Send many lines rapidly — only the first should emit progress (rate limit = 500ms)
        for (int i = 0; i < 50; i++)
        {
            await forwarder.OnStderrLineAsync($"line {i}");
        }

        // The first line always sends; subsequent lines within 500ms are skipped
        Assert.True(progressValues.Count < 50,
            $"Expected fewer progress notifications than lines due to rate limiting, but got {progressValues.Count} for 50 lines");
        Assert.True(progressValues.Count >= 1,
            "Expected at least one progress notification");

        // The first progress should have Progress = 1
        Assert.Equal(1, progressValues[0].Progress);
    }

    /// <summary>
    /// Simple ILogger implementation that captures log calls for assertion.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> LogEntries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add((logLevel, formatter(state, exception)));
        }
    }
}
