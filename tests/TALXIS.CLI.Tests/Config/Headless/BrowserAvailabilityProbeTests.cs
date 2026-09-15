using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Headless;

public class BrowserAvailabilityProbeTests
{
    [Fact]
    public void BrowserAvailable_ByDefault_OnNonLinux()
    {
        // When nothing signals browser isolation, the default is "available"
        // (this test runs on CI which may be Linux — it validates the env-var path).
        var probe = new BrowserAvailabilityProbe(new FakeEnv());
        if (!OperatingSystem.IsLinux())
        {
            Assert.True(probe.IsBrowserAvailable);
            Assert.Null(probe.UnavailableReason);
        }
    }

    [Theory]
    [InlineData("TXC_TTY_ONLY", "1")]
    [InlineData("TXC_TTY_ONLY", "true")]
    public void TxcTtyOnly_ForcesBrowserUnavailable(string envName, string value)
    {
        var probe = new BrowserAvailabilityProbe(new FakeEnv((envName, value)));
        Assert.False(probe.IsBrowserAvailable);
        Assert.Contains("TXC_TTY_ONLY", probe.UnavailableReason);
    }

    [Fact]
    public void Codespaces_ForcesBrowserUnavailable()
    {
        var probe = new BrowserAvailabilityProbe(new FakeEnv(("CODESPACES", "true")));
        Assert.False(probe.IsBrowserAvailable);
        Assert.Contains("CODESPACES", probe.UnavailableReason);
    }

    [Fact]
    public void Codespaces_FalsyValue_DoesNotForce()
    {
        var probe = new BrowserAvailabilityProbe(new FakeEnv(("CODESPACES", "false")));
        // On non-Linux, should be available. On Linux, depends on DISPLAY.
        if (!OperatingSystem.IsLinux())
            Assert.True(probe.IsBrowserAvailable);
    }

    [Fact]
    public void Linux_NoDisplay_BrowserUnavailable()
    {
        if (!OperatingSystem.IsLinux())
            return; // Test only meaningful on Linux

        var probe = new BrowserAvailabilityProbe(new FakeEnv());
        // On Linux CI without DISPLAY, the probe should detect browser isolation.
        Assert.False(probe.IsBrowserAvailable);
        Assert.Contains("DISPLAY", probe.UnavailableReason!);
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
