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
    [InlineData("component type list")]
    [InlineData("component type explain Entity")]
    [InlineData("workspace component parameter-list pp-entity")]
    [InlineData("environment package import --help")]
    [InlineData("environment package uninstall --help")]
    [InlineData("environment solution import --help")]
    [InlineData("environment solution uninstall --help")]
    [InlineData("environment solution list --help")]
    [InlineData("environment deployment list --help")]
    [InlineData("environment deployment show --help")]
    [InlineData("environment --help")]
    public async Task Command_ExecutesSuccessfully(string command)
    {
        var output = await CliRunner.RunAsync(command);
        
        Assert.NotNull(output);
        Assert.NotEmpty(output.Trim());
    }

    [Fact]
    public async Task ComponentTypeList_ContainsExpectedTypes()
    {
        var output = await CliRunner.RunAsync("component type list");
        
        Assert.Contains("Entity", output);
    }

    [Fact]
    public async Task ComponentTypeExplain_ReturnsTypeDetails()
    {
        var output = await CliRunner.RunAsync("component type explain Entity");
        
        // Output is JSON when stdout is redirected (piped) — the TxcLeafCommand
        // base auto-detects format, so integration tests see JSON instead of plain text.
        Assert.Contains("Entity", output);
        Assert.Contains("Table", output); // alias
    }
}
