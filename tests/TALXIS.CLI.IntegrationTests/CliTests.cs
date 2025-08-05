using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Tests for CLI command execution.
/// </summary>
public class CliTests
{
    [Theory]
    [InlineData("workspace component list")]
    [InlineData("workspace component explain pp-entity")]
    [InlineData("workspace component parameter list pp-entity")]
    public async Task Command_ExecutesSuccessfully(string command)
    {
        var output = await CliRunner.RunAsync(command);
        
        Assert.NotNull(output);
        Assert.NotEmpty(output.Trim());
    }

    [Fact]
    public async Task WorkspaceComponentList_ContainsExpectedComponents()
    {
        var output = await CliRunner.RunAsync("workspace component list");
        
        Assert.Contains("Available components:", output);
        Assert.Contains("pp-entity", output);
        Assert.Contains("short name:", output);
    }

    [Fact]
    public async Task WorkspaceComponentExplain_ReturnsComponentDetails()
    {
        var output = await CliRunner.RunAsync("workspace component explain pp-entity");
        
        Assert.Contains("Name:", output);
        Assert.Contains("Short names:", output);
        Assert.Contains("Description:", output);
        Assert.Contains("pp-entity", output);
    }
}
