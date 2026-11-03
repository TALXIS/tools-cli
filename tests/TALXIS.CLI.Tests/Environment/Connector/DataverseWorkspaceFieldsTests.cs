using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using TALXIS.CLI.Features.Environment.Connector.Operation.Output;
using TALXIS.CLI.Features.Environment.Connector.Operation.Parameter;
using TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

/// <summary>
/// Dataverse row fields of a workspace table, from the lab's Entity.xml,
/// against what the connector answered through a connection on wm-dev-2102
/// for the same table (<c>dataverse-warehouseitem-fields.json</c>).
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class DataverseWorkspaceFieldsTests : IDisposable
{
    private const string Dataverse = "shared_commondataserviceforapps";
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Environment", "Connector", "Fixtures");

    private readonly string _root = Directory.CreateTempSubdirectory("txc-dv-").FullName;
    private readonly ConnectionCommandTestHost _host;
    private readonly JsonElement _live = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "dataverse-warehouseitem-fields.json"))).RootElement;

    public DataverseWorkspaceFieldsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Copy(Path.Combine(Fixtures, "datamodel"), Path.Combine(_root, "src", "Solutions.DataModel"));
        _host = new ConnectionCommandTestHost { CurrentDirectory = _root };
        _host.Operations.Operation = (_, operation) => Operation(operation);
    }

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("UpdateOnlyRecord")]
    [InlineData("CreateRecord")]
    public async Task ItemFields_MatchTheConnectorAnswer_WithNoConnection(string operation)
    {
        var answer = await ParameterGet(operation);

        Assert.Equal(DynamicValuesStatus.Fields, answer.GetProperty("status").GetString());
        Assert.Equal("workspace", answer.GetProperty("source").GetString());
        Assert.Equal("src/Solutions.DataModel/Entities/lab_warehouseitem/Entity.xml", answer.GetProperty("sourcePath").GetString());
        Assert.False(answer.TryGetProperty("connectionReference", out var reference) && reference.ValueKind != JsonValueKind.Null);

        var expected = _live.GetProperty(operation).EnumerateArray()
            .Select(f => (f.GetProperty("name").GetString(), f.GetProperty("type").GetString(), f.GetProperty("required").GetBoolean()))
            .OrderBy(f => f.Item1, StringComparer.Ordinal);
        var actual = answer.GetProperty("fields").EnumerateArray()
            .Select(f => (f.GetProperty("name").GetString(), f.GetProperty("type").GetString(), f.GetProperty("required").GetBoolean()))
            .OrderBy(f => f.Item1, StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ItemFields_LabelLookupsWithTheirTable_AndListOptionsInline()
    {
        var fields = (await ParameterGet("UpdateOnlyRecord")).GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);

        Assert.Equal("Location (Warehouse Locations)", fields["item/lab_locationid@odata.bind"].GetProperty("description").GetString());
        Assert.Equal("Owner (Owners)", fields["item/ownerid@odata.bind"].GetProperty("description").GetString());
        var category = fields["item/lab_category"].GetProperty("allowedValues").EnumerateArray().ToList();
        Assert.Equal(("100000002", "Food"), (category[2].GetProperty("value").GetString(), category[2].GetProperty("label").GetString()));
    }

    [Fact]
    public async Task ListRowFields_MatchTheConnectorAnswer()
    {
        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationOutputGetCliCommand
        {
            Connector = Dataverse, Operation = "ListRecords", Output = "body/value", Value = ["entityName=lab_warehouseitems"],
        });

        Assert.Equal(0, exit);
        var answer = JsonDocument.Parse(output).RootElement;
        Assert.Equal("workspace", answer.GetProperty("source").GetString());
        var expected = _live.GetProperty("ListRecords").EnumerateArray()
            .Select(o => (o.GetProperty("key").GetString(), o.GetProperty("type").GetString())).OrderBy(o => o.Item1, StringComparer.Ordinal);
        var outputs = answer.GetProperty("outputs").EnumerateArray().ToList();
        Assert.Equal(expected, outputs.Select(o => (o.GetProperty("key").GetString(), o.GetProperty("type").GetString())).OrderBy(o => o.Item1, StringComparer.Ordinal));
        Assert.Contains(outputs, o => o.GetProperty("expression").GetString() == "@items('<loop over body/value>')?['lab_needsrestock']");
    }

    [Fact]
    public async Task TableOutsideTheWorkspace_FallsBackToTheConnection()
    {
        _host.References.Record = _host.References.Record with { ApiId = Dataverse };
        _host.Operations.Resolve = request => new OperationResolution(request.Operation, ConnectionUsed: false,
            [new DynamicLookup("item", "fields", "GetMetadataForGetEntity", DynamicLookupStatus.Planned, null)]);

        var answer = await ParameterGet("UpdateOnlyRecord", "contacts");

        // The connection path: the user is asked before their connection is used.
        Assert.Equal(DynamicValuesStatus.NeedsConsent, answer.GetProperty("status").GetString());
        Assert.NotEqual("workspace", answer.GetProperty("source").GetString());
    }

    private async Task<JsonElement> ParameterGet(string operation, string table = "lab_warehouseitems")
    {
        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationParameterGetCliCommand
        {
            Connector = Dataverse, Operation = operation, Parameter = "item", Value = [$"entityName={table}"],
        });
        Assert.Equal(0, exit);
        return JsonDocument.Parse(output).RootElement;
    }

    /// <summary>The operation as the environment describes it: only the dynamic row, the part this replaces.</summary>
    private static OperationDetail Operation(string operation)
    {
        var entitySet = new DynamicValuesRef("GetEntitySets", new Dictionary<string, string?>(), "value", "EntitySetName", "DisplayName");
        var row = new DynamicValuesRef("GetMetadataForGetEntity", new Dictionary<string, string?> { ["entityName"] = "$ref:entityName" }, ValuePath: "schema");
        return new OperationDetail(Dataverse, Dataverse, operation, operation, null, "OpenApiConnection", false,
            [
                new OperationParameter("entityName", "string", true, "Table name", null, null, entitySet, null, null),
                new OperationParameter("item", "object", true, "Row item", null, null, null, null, row),
            ],
            Outputs: [new OperationParameter("body/value", "array", false, "List of Items", null, null, null, null, row)]);
    }

    private static void Copy(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
