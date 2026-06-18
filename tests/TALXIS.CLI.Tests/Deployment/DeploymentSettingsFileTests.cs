using System.IO;
using TALXIS.CLI.Core.Deployment;
using Xunit;

namespace TALXIS.CLI.Tests.Deployment;

public class DeploymentSettingsFileTests
{
    [Fact]
    public void Parse_ReadsPascalCaseFile()
    {
        const string json = """
            {
              "EnvironmentVariables": [
                { "SchemaName": "tst_env", "Value": "UAT" }
              ],
              "ConnectionReferences": [
                { "LogicalName": "tst_sp", "ConnectionId": "abc", "ConnectorId": "/providers/x/shared_sharepointonline" }
              ]
            }
            """;

        var settings = DeploymentSettingsFile.Parse(json);

        var variable = Assert.Single(settings.EnvironmentVariables);
        Assert.Equal("tst_env", variable.SchemaName);
        Assert.Equal("UAT", variable.Value);

        var connection = Assert.Single(settings.ConnectionReferences);
        Assert.Equal("tst_sp", connection.LogicalName);
        Assert.Equal("abc", connection.ConnectionId);
        Assert.Equal("/providers/x/shared_sharepointonline", connection.ConnectorId);
    }

    [Fact]
    public void Parse_AlsoAcceptsCamelCase()
    {
        const string json = """
            {
              "environmentVariables": [ { "schemaName": "tst_env", "value": "v" } ],
              "connectionReferences": [ { "logicalName": "tst_sp", "connectionId": "abc" } ]
            }
            """;

        var settings = DeploymentSettingsFile.Parse(json);

        Assert.Equal("tst_env", Assert.Single(settings.EnvironmentVariables).SchemaName);
        Assert.Equal("tst_sp", Assert.Single(settings.ConnectionReferences).LogicalName);
    }

    [Fact]
    public void Parse_AllowsMissingSections()
    {
        var settings = DeploymentSettingsFile.Parse("{}");

        Assert.Empty(settings.ConnectionReferences);
        Assert.Empty(settings.EnvironmentVariables);
        Assert.True(settings.IsEmpty);
    }

    [Fact]
    public void Parse_ThrowsOnMalformedJson()
    {
        Assert.Throws<InvalidDataException>(() => DeploymentSettingsFile.Parse("{ not json"));
    }

    [Fact]
    public void Parse_ThrowsWhenConnectionMissesLogicalName()
    {
        const string json = """{ "ConnectionReferences": [ { "ConnectionId": "abc" } ] }""";

        var ex = Assert.Throws<InvalidDataException>(() => DeploymentSettingsFile.Parse(json));
        Assert.Contains("LogicalName", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsWhenVariableMissesSchemaName()
    {
        const string json = """{ "EnvironmentVariables": [ { "Value": "v" } ] }""";

        var ex = Assert.Throws<InvalidDataException>(() => DeploymentSettingsFile.Parse(json));
        Assert.Contains("SchemaName", ex.Message);
    }

    [Fact]
    public void TryLoad_ReturnsFalseWithErrorWhenFileMissing()
    {
        var ok = DeploymentSettingsFile.TryLoad(
            Path.Combine(Path.GetTempPath(), "txc-does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"),
            out var settings,
            out var error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
