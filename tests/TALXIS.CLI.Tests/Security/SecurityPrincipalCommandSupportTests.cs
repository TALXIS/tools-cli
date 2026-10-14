using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Security;

/// <summary>
/// Direct unit coverage for the shared <see cref="SecurityPrincipalCommandSupport.TryHandleValidationException"/>
/// helper, reused by every <c>txc security</c> user/app/group/role command
/// (previously duplicated per command-support class).
/// </summary>
public sealed class SecurityPrincipalCommandSupportTests
{
    [Fact]
    public void TryHandleValidationException_AmbiguousPrincipal_LogsCandidates()
    {
        var logger = new RecordingLogger();

        var handled = SecurityPrincipalCommandSupport.TryHandleValidationException(
            logger,
            new TenantPrincipalAmbiguousException(
                PowerPlatformPrincipalType.ApplicationUser,
                "Contoso CLI",
                [
                    "Contoso CLI (appId: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa, id: 11111111-1111-1111-1111-111111111111)",
                    "Contoso CLI (appId: bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb, id: 22222222-2222-2222-2222-222222222222)"
                ]),
            out var exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.Contains(logger.Messages, message => message.Contains("Candidate: Contoso CLI (appId: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa, id: 11111111-1111-1111-1111-111111111111)", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("Candidate: Contoso CLI (appId: bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb, id: 22222222-2222-2222-2222-222222222222)", StringComparison.Ordinal));
    }

    [Fact]
    public void TryHandleValidationException_AmbiguousRole_LogsCandidates()
    {
        var logger = new RecordingLogger();

        var handled = SecurityPrincipalCommandSupport.TryHandleValidationException(
            logger,
            new TenantRoleAmbiguousException("Owner", ["Owner", "Owner"]),
            out var exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.Contains(logger.Messages, message => message.Contains("Candidate: Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void TryHandleValidationException_ArgumentException_ReturnsValidationError()
    {
        var logger = new RecordingLogger();

        var handled = SecurityPrincipalCommandSupport.TryHandleValidationException(
            logger,
            new ArgumentException("Group 'not-a-guid' must be specified as an Entra object id (GUID)."),
            out var exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.Contains(logger.Messages, message => message.Contains("Group 'not-a-guid' must be specified as an Entra object id (GUID).", StringComparison.Ordinal));
    }

    [Fact]
    public void TryHandleValidationException_InvalidOperationException_ReturnsValidationError()
    {
        var logger = new RecordingLogger();

        var handled = SecurityPrincipalCommandSupport.TryHandleValidationException(
            logger,
            new InvalidOperationException("Something went wrong."),
            out var exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void TryHandleValidationException_UnrelatedException_ReturnsFalse()
    {
        var logger = new RecordingLogger();

        var handled = SecurityPrincipalCommandSupport.TryHandleValidationException(
            logger,
            new NotSupportedException("Unsupported."),
            out var exitCode);

        Assert.False(handled);
        Assert.Equal(0, exitCode);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
