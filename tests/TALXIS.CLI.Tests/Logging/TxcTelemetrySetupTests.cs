using TALXIS.CLI.Logging;
using Xunit;

namespace TALXIS.CLI.Tests.Logging;

public class TxcTelemetrySetupTests
{
    [Fact]
    public void ResolveServiceSuffix_WithoutOverride_UsesEntryPoint()
    {
        var original = System.Environment.GetEnvironmentVariable("TXC_SERVICE_SUFFIX");
        try
        {
            System.Environment.SetEnvironmentVariable("TXC_SERVICE_SUFFIX", null);

            var suffix = TxcTelemetrySetup.ResolveServiceSuffix("mcp");

            Assert.Equal("mcp", suffix);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TXC_SERVICE_SUFFIX", original);
        }
    }

    [Fact]
    public void ResolveServiceSuffix_WithOverride_UsesHostServiceSuffix()
    {
        var original = System.Environment.GetEnvironmentVariable("TXC_SERVICE_SUFFIX");
        try
        {
            System.Environment.SetEnvironmentVariable("TXC_SERVICE_SUFFIX", "cli");

            var suffix = TxcTelemetrySetup.ResolveServiceSuffix("mcp");

            Assert.Equal("cli", suffix);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TXC_SERVICE_SUFFIX", original);
        }
    }
}
