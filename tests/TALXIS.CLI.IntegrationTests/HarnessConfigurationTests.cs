using System;
using System.Linq;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

[Collection("Sequential")]
public class HarnessConfigurationTests
{
    [Fact]
    public void CliRunner_UsesCurrentTestBuildConfiguration()
    {
        var psi = CliRunner.CreateProcessStartInfo(new[] { "--help" });
        var arguments = psi.ArgumentList.ToList();
        var configurationFlagIndex = arguments.IndexOf("--configuration");

        Assert.True(configurationFlagIndex >= 0);
        Assert.Equal(TestExecutionContext.BuildConfiguration, arguments[configurationFlagIndex + 1]);
        Assert.Contains("--no-build", arguments);
    }

    [Fact]
    public void McpTestClient_UsesCurrentTestBuildConfiguration()
    {
        var arguments = McpTestClient.GetCommandArguments("dummy.csproj");
        var configurationFlagIndex = Array.IndexOf(arguments, "--configuration");

        Assert.True(configurationFlagIndex >= 0);
        Assert.Equal(TestExecutionContext.BuildConfiguration, arguments[configurationFlagIndex + 1]);
        Assert.Contains("--no-build", arguments);
    }
}
