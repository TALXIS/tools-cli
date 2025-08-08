using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Tests for CLI command execution.
/// </summary>
[Collection("Sequential")]
public class CliTests
{
    [Theory]
    [InlineData("workspace component type list")]
    [InlineData("workspace component type explain pp-entity")]
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
        var output = await CliRunner.RunAsync("workspace component type list");
        
        Assert.Contains("pp-entity", output);
    }

    [Fact]
    public async Task WorkspaceComponentType_ReturnsComponentDetails()
    {
        var output = await CliRunner.RunAsync("workspace component type explain pp-entity");
        
        Assert.Contains("Type:", output);
        Assert.Contains("Description:", output);
        Assert.Contains("pp-entity", output);
    }
}
