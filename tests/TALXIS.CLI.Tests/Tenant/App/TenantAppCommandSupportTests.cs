using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Tenant.App;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.App;

public sealed class TenantAppCommandSupportTests
{
    [Fact]
    public void TryHandleValidationException_AmbiguousPrincipal_LogsCandidates()
    {
        var logger = new RecordingLogger();

        var handled = TenantAppCommandSupport.TryHandleValidationException(
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

        var handled = TenantAppCommandSupport.TryHandleValidationException(
            logger,
            new TenantRoleAmbiguousException("Owner", ["Owner", "Owner"]),
            out var exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.Contains(logger.Messages, message => message.Contains("Candidate: Owner", StringComparison.Ordinal));
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
