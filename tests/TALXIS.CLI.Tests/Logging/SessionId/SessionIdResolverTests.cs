using Xunit;
using TALXIS.CLI.Logging.SessionId;

namespace TALXIS.CLI.Tests.Logging.SessionId;

public class SessionIdResolverTests
{
    [Fact]
    public void Resolve_WithExplicitEnvVar_ReturnsExplicitValue()
    {
        var strategies = new ISessionIdStrategy[]
        {
            new FakeStrategy("explicit", "my-session-123"),
            new FakeStrategy("copilot", "copilot-session"),
            new FakeStrategy("fallback", "generated-id"),
        };

        var resolver = new SessionIdResolver(strategies);

        Assert.Equal("my-session-123", resolver.SessionId);
        Assert.Equal("explicit", resolver.Source);
    }

    [Fact]
    public void Resolve_SkipsNullStrategies_ReturnsCopilot()
    {
        var strategies = new ISessionIdStrategy[]
        {
            new FakeStrategy("explicit", value: null),
            new FakeStrategy("copilot", "copilot-session-456"),
            new FakeStrategy("fallback", "generated-id"),
        };

        var resolver = new SessionIdResolver(strategies);

        Assert.Equal("copilot-session-456", resolver.SessionId);
        Assert.Equal("copilot", resolver.Source);
    }

    [Fact]
    public void Resolve_AllStrategiesFail_UsesLast()
    {
        var strategies = new ISessionIdStrategy[]
        {
            new FakeStrategy("explicit", value: null),
            new FakeStrategy("copilot", value: null),
            new FakeStrategy("claude-code", value: null),
            new FakeStrategy("terminal", value: null),
            new FakeStrategy("generated", "fallback-uuid"),
        };

        var resolver = new SessionIdResolver(strategies);

        Assert.Equal("fallback-uuid", resolver.SessionId);
        Assert.Equal("generated", resolver.Source);
    }

    [Fact]
    public void Resolve_CachesResult_ReturnsSameValueOnRepeatedCalls()
    {
        var callCount = 0;
        var strategies = new ISessionIdStrategy[]
        {
            new FakeStrategy("counter", () =>
            {
                callCount++;
                return $"session-{callCount}";
            }),
        };

        var resolver = new SessionIdResolver(strategies);

        var first = resolver.SessionId;
        var second = resolver.SessionId;
        var source1 = resolver.Source;
        var source2 = resolver.Source;

        Assert.Equal(first, second);
        Assert.Equal(source1, source2);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Resolve_EmptyStrategies_GeneratesFallbackUuid()
    {
        // Edge case: no strategies at all — should still produce a valid session ID
        var resolver = new SessionIdResolver([]);

        Assert.NotNull(resolver.SessionId);
        Assert.Equal("generated", resolver.Source);
        Assert.True(Guid.TryParse(resolver.SessionId, out _));
    }

    [Fact]
    public void DefaultStrategies_ContainsFiveStrategiesInCorrectOrder()
    {
        var strategies = SessionIdResolver.DefaultStrategies;

        Assert.Equal(5, strategies.Count);
        Assert.IsType<ExplicitEnvVarStrategy>(strategies[0]);
        Assert.IsType<CopilotSessionStrategy>(strategies[1]);
        Assert.IsType<ClaudeCodeSessionStrategy>(strategies[2]);
        Assert.IsType<TerminalSessionStrategy>(strategies[3]);
        Assert.IsType<FallbackStrategy>(strategies[4]);
    }

    [Fact]
    public void FallbackStrategy_AlwaysReturnsValidGuid()
    {
        var strategy = new FallbackStrategy();

        var result = strategy.TryResolve();

        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result, out _));
        Assert.Equal("generated", strategy.Source);
    }

    [Fact]
    public void FallbackStrategy_GeneratesUniqueIds()
    {
        var strategy = new FallbackStrategy();

        var id1 = strategy.TryResolve();
        var id2 = strategy.TryResolve();

        Assert.NotEqual(id1, id2);
    }

    /// <summary>
    /// Fake strategy for testing that returns a predetermined value.
    /// </summary>
    private sealed class FakeStrategy : ISessionIdStrategy
    {
        private readonly Func<string?> _resolveFunc;

        public FakeStrategy(string source, string? value) : this(source, () => value) { }

        public FakeStrategy(string source, Func<string?> resolveFunc)
        {
            Source = source;
            _resolveFunc = resolveFunc;
        }

        public string Source { get; }
        public string? TryResolve() => _resolveFunc();
    }
}
