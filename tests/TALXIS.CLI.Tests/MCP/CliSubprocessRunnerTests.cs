using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

/// <summary>
/// Regression coverage for the forced-headless contract every MCP-spawned txc
/// subprocess must honor: interactive/device-code sign-in flows must never be
/// reachable when txc is invoked through MCP, because stdout is reserved for
/// JSON-RPC frames and no human is watching the terminal. This is enforced by
/// unconditionally setting TXC_NON_INTERACTIVE=1 on every subprocess
/// <see cref="System.Diagnostics.ProcessStartInfo"/>, which <see cref="HeadlessDetector"/>
/// then uses to block <see cref="TALXIS.CLI.Core.Headless.HeadlessAuthRequiredException"/>-guarded
/// interactive credential kinds. See src/TALXIS.CLI.MCP/README.md#auth-contract.
/// </summary>
public class CliSubprocessRunnerTests
{
    [Fact]
    public void BuildStartInfo_AlwaysForcesNonInteractiveEnvVar()
    {
        var startInfo = CliSubprocessRunner.BuildStartInfo(new[] { "config", "profile", "list" });

        Assert.True(startInfo.Environment.TryGetValue("TXC_NON_INTERACTIVE", out var value));
        Assert.Equal("1", value);
    }

    [Fact]
    public void BuildStartInfo_ForcesNonInteractiveEnvVar_RegardlessOfCliArgs()
    {
        // Even for commands that don't touch auth at all, the flag must still be
        // present — this is a blanket subprocess-level guarantee, not something
        // decided per-command.
        var startInfo = CliSubprocessRunner.BuildStartInfo(new[] { "config", "auth", "login", "--device-code" });

        Assert.True(startInfo.Environment.TryGetValue("TXC_NON_INTERACTIVE", out var value));
        Assert.Equal("1", value);
    }

    [Fact]
    public void BuildStartInfo_RedirectsStandardStreams()
    {
        // Redirected stdin/stdout is one of the two conditions HeadlessDetector
        // checks; redirected output alone (with TXC_NON_INTERACTIVE also set)
        // makes the headless determination doubly robust for the MCP path.
        var startInfo = CliSubprocessRunner.BuildStartInfo(new[] { "config", "profile", "list" });

        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
    }
}
