using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Headless;

[Collection("Sequential")]
public class AuthRequiredExitCodeTests
{
    private sealed class ThrowingCommand : TxcLeafCommand
    {
        private readonly Exception _toThrow;
        public ThrowingCommand(Exception toThrow) { _toThrow = toThrow; }
        protected override ILogger Logger { get; } = NullLogger.Instance;
        protected override Task<int> ExecuteAsync() => throw _toThrow;
    }

    [Fact]
    public async Task EnvironmentAuthRequired_MapsToExitCode3()
    {
        using var _ = new CommandTestHost();
        var cmd = new ThrowingCommand(
            new EnvironmentAuthRequiredException("https://devbox.crm4.dynamics.com"));

        var exit = await cmd.RunAsync();

        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task WrappedEnvironmentAuthRequired_MapsToExitCode3()
    {
        using var _ = new CommandTestHost();
        var inner = new EnvironmentAuthRequiredException("https://devbox.crm4.dynamics.com");
        var cmd = new ThrowingCommand(new InvalidOperationException("service wrapper", inner));

        var exit = await cmd.RunAsync();

        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task HeadlessAuthRequired_StaysExitCode1()
    {
        // Exit 3 is scoped to EnvironmentAuthRequiredException only.
        using var _ = new CommandTestHost();
        var cmd = new ThrowingCommand(
            new HeadlessAuthRequiredException(CredentialKind.InteractiveBrowser, "CI=true"));

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task UnrelatedError_StillMapsToExitCode1()
    {
        using var _ = new CommandTestHost();
        var cmd = new ThrowingCommand(new InvalidOperationException("boom"));

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }
}
