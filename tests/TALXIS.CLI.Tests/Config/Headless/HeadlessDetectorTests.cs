using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Headless;

public class HeadlessDetectorTests
{
    [Fact]
    public void InteractiveByDefault()
    {
        var det = new HeadlessDetector(new FakeProbe(false, false), new FakeEnv());
        Assert.False(det.IsHeadless);
        Assert.Null(det.Reason);
    }

    [Fact]
    public void StdinAndStdoutRedirectedMarksHeadless()
    {
        var det = new HeadlessDetector(new FakeProbe(true, true), new FakeEnv());
        Assert.True(det.IsHeadless);
        Assert.Contains("redirected", det.Reason);
    }

    [Fact]
    public void OnlyStdinRedirectedStaysInteractive()
    {
        var det = new HeadlessDetector(new FakeProbe(true, false), new FakeEnv());
        Assert.False(det.IsHeadless);
    }

    [Theory]
    [InlineData("TXC_NON_INTERACTIVE", "1")]
    [InlineData("TXC_NON_INTERACTIVE", "true")]
    [InlineData("CI", "true")]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("TF_BUILD", "True")]
    public void TruthyEnvVarForcesHeadless(string envName, string value)
    {
        var det = new HeadlessDetector(new FakeProbe(false, false),
            new FakeEnv((envName, value)));
        Assert.True(det.IsHeadless);
        Assert.Contains(envName, det.Reason);
    }

    [Fact]
    public void FalsyEnvVarDoesNotForceHeadless()
    {
        var det = new HeadlessDetector(new FakeProbe(false, false),
            new FakeEnv(("CI", "false")));
        Assert.False(det.IsHeadless);
    }

    private sealed class FakeProbe : IConsoleRedirectionProbe
    {
        public FakeProbe(bool input, bool output) { IsInputRedirected = input; IsOutputRedirected = output; }
        public bool IsInputRedirected { get; }
        public bool IsOutputRedirected { get; }
    }

    private sealed class FakeEnv : IEnvironmentReader
    {
        private readonly Dictionary<string, string> _map;
        public FakeEnv(params (string Key, string Value)[] entries)
        {
            _map = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }
        public string? Get(string name) => _map.TryGetValue(name, out var v) ? v : null;
        public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
    }
}
