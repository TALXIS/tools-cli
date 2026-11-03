using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Features.Environment.Connector;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using TALXIS.CLI.Features.Environment.Connector.Operation.Parameter;
using TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

/// <summary>
/// A workspace laid out like the lab: a DevKit connector project and the
/// solution that includes it. The environment stand-in throws on every call,
/// so an answer proves nothing was fetched.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class WorkspaceConnectorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("txc-ws-").FullName;

    public WorkspaceConnectorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Write("WarehouseManagement.slnx", "<Solution />");
        Write("src/Connectors.OpenFoodFacts/Connectors.OpenFoodFacts.csproj",
            """<Project Sdk="TALXIS.DevKit.Build.Sdk/1.11.1"><PropertyGroup><ProjectType>Connector</ProjectType></PropertyGroup></Project>""");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Environment", "Connector", "Fixtures", "openfoodfacts.swagger.json"),
            Path.Combine(_root, "src/Connectors.OpenFoodFacts/apiDefinition.swagger.json"));
        Write("src/Solutions.Connectors/Other/Solution.xml", """
            <ImportExportXml><SolutionManifest><RootComponents>
              <RootComponent type="372" schemaName="lab_connectorsopenfoodfacts" behavior="0" />
              <RootComponent type="1" schemaName="lab_connectorsopenfoodfacts_other" behavior="0" />
            </RootComponents></SolutionManifest></ImportExportXml>
            """);
        // Not connectors: a plain project, build output, and a connector with no definition.
        Write("src/Plugins.Warehouse/Plugins.Warehouse.csproj", "<Project><PropertyGroup><ProjectType>Plugin</ProjectType></PropertyGroup></Project>");
        Write("src/Connectors.OpenFoodFacts/bin/Debug/Connectors.Copy.csproj", "<Project><PropertyGroup><ProjectType>Connector</ProjectType></PropertyGroup></Project>");
        Write("src/Connectors.Empty/Connectors.Empty.csproj", "<Project><PropertyGroup><ProjectType>Connector</ProjectType></PropertyGroup></Project>");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Locator_FindsTheConnectorProject_AndItsLogicalNameFromTheSolution()
    {
        var connector = Assert.Single(WorkspaceConnectorLocator.FindAll(_root));

        Assert.Equal("lab_connectorsopenfoodfacts", connector.LogicalName);
        Assert.Equal("OpenFoodFacts", connector.ConnectorName);
        Assert.Equal("Connectors.OpenFoodFacts", connector.ProjectName);
        Assert.Equal(Path.Combine(_root, "src/Connectors.OpenFoodFacts/apiDefinition.swagger.json"), connector.DefinitionPath);
    }

    [Theory]
    [InlineData("lab_connectorsopenfoodfacts")]
    [InlineData("OpenFoodFacts")]
    [InlineData("connectors.openfoodfacts")]
    public void Locator_MatchesLogicalConnectorOrProjectName(string name)
        => Assert.NotNull(WorkspaceConnectorLocator.Find(_root, name));

    [Fact]
    public void Locator_DoesNotClaimBuiltInConnectors()
        => Assert.Null(WorkspaceConnectorLocator.Find(_root, "shared_commondataserviceforapps"));

    [Fact]
    public void Locator_HonoursAnExplicitConnectorNameAndDefinitionPath()
    {
        Write("src/Connectors.Custom/Connectors.Custom.csproj",
            "<Project><PropertyGroup><ProjectType>Connector</ProjectType><ConnectorName>Stock</ConnectorName><ConnectorApiDefinitionPath>def/api.json</ConnectorApiDefinitionPath></PropertyGroup></Project>");
        Write("src/Connectors.Custom/def/api.json", "{}");

        var connector = WorkspaceConnectorLocator.Find(_root, "Stock");

        Assert.NotNull(connector);
        Assert.Null(connector.LogicalName);
        Assert.Equal("Stock", connector.Identifier);
        Assert.Equal(Path.Combine(_root, "src/Connectors.Custom/def/api.json"), connector.DefinitionPath);
    }

    [Fact]
    public void WorkspaceRoot_PrefersTxcWorkspace_ThenGit()
    {
        var nested = Path.Combine(_root, "src", "Connectors.OpenFoodFacts");
        Assert.Equal(_root, WorkspaceConnectorLocator.FindWorkspaceRoot(nested));

        Write("src/.txc/workspace.json", "{}");
        Assert.Equal(Path.Combine(_root, "src"), WorkspaceConnectorLocator.FindWorkspaceRoot(nested));
    }

    [Fact]
    public async Task OperationGet_AnswersFromTheWorkspace_WithNoEnvironmentCall()
    {
        using var host = new ConnectionCommandTestHost { CurrentDirectory = Path.Combine(_root, "src") };

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationGetCliCommand
        {
            Connector = "lab_connectorsopenfoodfacts",
            Operation = "GetProductByBarcode",
        });

        Assert.Equal(0, exit);
        var spec = JsonDocument.Parse(output).RootElement;
        Assert.Equal("workspace", spec.GetProperty("source").GetString());
        Assert.Equal("src/Connectors.OpenFoodFacts/apiDefinition.swagger.json", spec.GetProperty("sourcePath").GetString());
        Assert.Equal("barcode", spec.GetProperty("parameters")[0].GetProperty("name").GetString());

        // No environment id exists locally: the skeleton binds by connection reference.
        var actions = spec.GetProperty("snippet").GetProperty("actions");
        var host_ = actions.EnumerateObject().Single().Value.GetProperty("inputs").GetProperty("host");
        Assert.StartsWith("<resolved at deploy", host_.GetProperty("apiId").GetString());
        Assert.Equal("lab_connectorsopenfoodfacts", host_.GetProperty("connectionName").GetString());
    }

    [Fact]
    public async Task ConnectorGet_IndexesTheWorkspaceDefinition()
    {
        using var host = new ConnectionCommandTestHost { CurrentDirectory = _root };

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorGetCliCommand { Connector = "OpenFoodFacts" });

        Assert.Equal(0, exit);
        var detail = JsonDocument.Parse(output).RootElement;
        Assert.Equal("workspace", detail.GetProperty("source").GetString());
        Assert.Equal("lab_connectorsopenfoodfacts", detail.GetProperty("logicalName").GetString());
        Assert.Equal(2, detail.GetProperty("operationCount").GetInt32());
    }

    [Fact]
    public async Task ParameterGet_StaticParameter_IsAnsweredFromTheWorkspace()
    {
        using var host = new ConnectionCommandTestHost { CurrentDirectory = _root };

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationParameterGetCliCommand
        {
            Connector = "lab_connectorsopenfoodfacts",
            Operation = "GetProductByBarcode",
            Parameter = "barcode",
        });

        Assert.Equal(0, exit);
        var answer = JsonDocument.Parse(output).RootElement;
        Assert.Equal(DynamicValuesStatus.Static, answer.GetProperty("status").GetString());
        Assert.Equal("workspace", answer.GetProperty("source").GetString());
    }

    [Fact]
    public async Task OutsideTheWorkspace_TheEnvironmentIsAsked()
    {
        using var host = new ConnectionCommandTestHost { CurrentDirectory = Path.GetTempPath() };

        await Assert.ThrowsAsync<NotSupportedException>(() => ConnectorMetadataLookup.GetOperationAsync(
            null, "lab_connectorsopenfoodfacts", "GetProductByBarcode", CancellationToken.None));
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
