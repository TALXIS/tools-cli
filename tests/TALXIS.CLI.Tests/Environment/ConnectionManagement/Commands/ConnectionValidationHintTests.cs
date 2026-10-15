using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connection;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;

/// <summary>
/// The CLI remedy for a typed platform error is appended to the message the
/// command logs, since the platform layer must not name commands itself.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class ConnectionValidationHintTests
{
    [Fact]
    public async Task ANotFoundConnection_LogsTheMessageFollowedByTheCommandThatHelps()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.Failure = new ConnectionNotFoundException("c1");
        var command = new LoggingGetCommand { ConnectionId = "c1" };

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(command);

        Assert.Equal(2, exit);
        var error = Assert.Single(command.Log.Errors);
        Assert.StartsWith(new ConnectionNotFoundException("c1").Message, error);
        Assert.EndsWith("Run 'txc environment connection list' to see the connections you can use.", error);
    }

    [Fact]
    public async Task AnErrorWithoutARemedy_LogsOnlyTheMessage()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.Failure = new ArgumentException("bad input");
        var command = new LoggingGetCommand { ConnectionId = "c1" };

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(command);

        Assert.Equal(2, exit);
        Assert.Equal("bad input", Assert.Single(command.Log.Errors));
    }

    private sealed class LoggingGetCommand : ConnectionGetCliCommand
    {
        public ErrorLog Log { get; } = new();

        protected override ILogger Logger => Log;
    }

    private sealed class ErrorLog : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
