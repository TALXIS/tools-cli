using System.Text.Json;
using TALXIS.CLI.Features.Environment.Connector.Operation.Parameter;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

/// <summary>
/// The connector tools take positional arguments. Without an explicit <c>Name</c> on
/// <c>[CliArgument]</c> the MCP schema keys would be the PascalCase property names, unlike the
/// lower-case keys every option gets, so this pins the keys and the positional order.
/// </summary>
public class ConnectorToolInputSchemaTests
{
    [Fact]
    public void InputSchema_ParameterGet_UsesLowerCaseKeys()
    {
        var schema = new CliCommandAdapter().BuildInputSchema(typeof(ConnectorOperationParameterGetCliCommand));
        var keys = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("connector", keys);
        Assert.Contains("operation", keys);
        Assert.Contains("parameter", keys);
        Assert.Contains("filter", keys);
        Assert.DoesNotContain("Connector", keys);
        Assert.DoesNotContain("search", keys);
    }

    [Fact]
    public void BuildCliArgs_ParameterGet_PassesPositionalsInOrder()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["parameter"] = JsonSerializer.SerializeToElement("item"),
            ["connector"] = JsonSerializer.SerializeToElement("shared_commondataserviceforapps"),
            ["operation"] = JsonSerializer.SerializeToElement("CreateRecord"),
            ["filter"] = JsonSerializer.SerializeToElement("account"),
        };

        var cliArgs = new CliCommandAdapter().BuildCliArgs("environment_connector_operation_parameter_get", args);

        var connectorAt = cliArgs.IndexOf("shared_commondataserviceforapps");
        Assert.True(connectorAt >= 0);
        Assert.Equal("CreateRecord", cliArgs[connectorAt + 1]);
        Assert.Equal("item", cliArgs[connectorAt + 2]);
        Assert.Contains("--filter", cliArgs);
        Assert.Equal("account", cliArgs[cliArgs.IndexOf("--filter") + 1]);
    }
}
