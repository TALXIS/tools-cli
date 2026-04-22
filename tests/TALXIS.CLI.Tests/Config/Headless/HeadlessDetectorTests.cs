using TALXIS.CLI.Config.Headless;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Headless;

public class HeadlessDetectorTests
{
    [Fact]
    public void InteractiveByDefault()
    {
        var det = new HeadlessDetector(new FakeProbe(false, false), _ => null);
        Assert.False(det.IsHeadless);
        Assert.Null(det.Reason);
    }

    [Fact]
    public void StdinAndStdoutRedirectedMarksHeadless()
    {
        var det = new HeadlessDetector(new FakeProbe(true, true), _ => null);
        Assert.True(det.IsHeadless);
        Assert.Contains("redirected", det.Reason);
    }

    [Fact]
    public void OnlyStdinRedirectedStaysInteractive()
    {
        var det = new HeadlessDetector(new FakeProbe(true, false), _ => null);
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
            name => name == envName ? value : null);
        Assert.True(det.IsHeadless);
        Assert.Contains(envName, det.Reason);
    }

    [Fact]
    public void FalsyEnvVarDoesNotForceHeadless()
    {
        var det = new HeadlessDetector(new FakeProbe(false, false),
            name => name == "CI" ? "false" : null);
        Assert.False(det.IsHeadless);
    }

    private sealed class FakeProbe : IConsoleRedirectionProbe
    {
        public FakeProbe(bool input, bool output) { IsInputRedirected = input; IsOutputRedirected = output; }
        public bool IsInputRedirected { get; }
        public bool IsOutputRedirected { get; }
    }
}
