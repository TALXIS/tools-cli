using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connector;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

/// <summary>
/// The fetch-once snapshot in <c>.txc/connectors/</c>: the first answer comes
/// from the environment and is saved, later ones are read back, and
/// <c>--refresh</c> fetches again.
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class ConnectorSnapshotTests : IDisposable
{
    private const string Dataverse = "shared_commondataserviceforapps";

    private readonly string _root = Directory.CreateTempSubdirectory("txc-snap-").FullName;
    private readonly ConnectionCommandTestHost _host;

    public ConnectorSnapshotTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        _host = new ConnectionCommandTestHost { CurrentDirectory = _root };
        _host.Operations.Operation = (_, operation) => ListRecords(operation);
        _host.Operations.Connector = (connector, _) => new ConnectorDetail(connector, connector, "Microsoft Dataverse", 2, null,
        [
            new ConnectorOperationSummary("ListRecords", ConnectorOperationKind.Action, "List rows", "GET", "/rows", false),
            new ConnectorOperationSummary("CreateRecord", ConnectorOperationKind.Action, "Add a new row", "POST", "/rows", false),
        ]);
    }

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SecondOperationGet_ReadsTheSnapshot_WithoutFetching()
    {
        var first = await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);
        var second = await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "listrecords", CancellationToken.None);

        Assert.Equal([$"{Dataverse}/ListRecords"], _host.Operations.Fetches);
        Assert.Equal(ConnectorMetadataSource.Environment, first.Source);
        Assert.Equal(ConnectorMetadataSource.Snapshot, second.Source);
        Assert.Equal($".txc/connectors/{Dataverse}/ListRecords.json", second.SourcePath);
        Assert.True(File.Exists(Path.Combine(_root, second.SourcePath!)));

        // What a flow is written against round-trips, resolver arguments included.
        Assert.Equal(JsonSerializer.Serialize(first with { Source = null, SourcePath = null }),
            JsonSerializer.Serialize(second with { Source = null, SourcePath = null }));
        Assert.Equal("$ref?:$select", second.Outputs![0].DynamicSchema!.Parameters!["selectedEntityAttributes"]);
        Assert.Equal("Modified", second.Parameters[0].ValueLabels!["3"]);
    }

    [Fact]
    public async Task Snapshot_HoldsOnlyTheParsedOperation()
    {
        await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);

        var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, ".txc", "connectors", Dataverse, "ListRecords.json"))).RootElement;
        Assert.Equal(ConnectorSnapshotStore.SchemaVersion, file.GetProperty("schemaVersion").GetInt32());
        Assert.True(file.TryGetProperty("fetchedAt", out _));
        var operation = file.GetProperty("operation");
        Assert.False(operation.TryGetProperty("source", out _));
        Assert.False(operation.TryGetProperty("sourcePath", out _));
        Assert.Equal(["schemaVersion", "fetchedAt", "operation"], file.EnumerateObject().Select(p => p.Name));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".txc", "connectors", Dataverse), "*.tmp"));
    }

    [Fact]
    public async Task Refresh_FetchesAgain_AndRewritesTheSnapshot()
    {
        await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);
        _host.Operations.Operation = (_, operation) => ListRecords(operation) with { Summary = "List rows (changed)" };

        var refreshed = await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None, refresh: true);
        var after = await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);

        Assert.Equal(2, _host.Operations.Fetches.Count);
        Assert.Equal(ConnectorMetadataSource.Environment, refreshed.Source);
        Assert.Equal("List rows (changed)", after.Summary);
    }

    [Fact]
    public async Task ConnectorIndex_IsStoredWhole_AndFilteredOnRead()
    {
        var filtered = await ConnectorMetadataLookup.GetConnectorAsync(null, Dataverse, "add", CancellationToken.None);
        var again = await ConnectorMetadataLookup.GetConnectorAsync(null, Dataverse, "list", CancellationToken.None);

        Assert.Equal([Dataverse], _host.Operations.Fetches);
        Assert.Equal(["CreateRecord"], filtered.Operations.Select(o => o.OperationId));
        Assert.Equal(ConnectorMetadataSource.Snapshot, again.Source);
        Assert.Equal(["ListRecords"], again.Operations.Select(o => o.OperationId));
        Assert.Equal((2, 1), (again.OperationCount, again.MatchedOperations));
    }

    [Fact]
    public async Task UnreadableOrOtherVersionSnapshot_IsFetchedAgain()
    {
        var directory = Path.Combine(_root, ".txc", "connectors", Dataverse);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ListRecords.json"), """{ "schemaVersion": 99, "operation": {} }""");
        File.WriteAllText(Path.Combine(directory, "CreateRecord.json"), "{ not json");

        await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);
        await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "CreateRecord", CancellationToken.None);

        Assert.Equal(2, _host.Operations.Fetches.Count);
        Assert.Equal(1, JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ListRecords.json"))).RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task OutsideAWorkspace_NothingIsWritten_AndEveryCallFetches()
    {
        var outside = Directory.CreateTempSubdirectory("txc-nows-").FullName;
        try
        {
            _host.CurrentDirectory = outside;
            await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);
            var second = await ConnectorMetadataLookup.GetOperationAsync(null, Dataverse, "ListRecords", CancellationToken.None);

            Assert.Equal(2, _host.Operations.Fetches.Count);
            Assert.Equal(ConnectorMetadataSource.Environment, second.Source);
            Assert.Null(second.SourcePath);
            Assert.False(Directory.Exists(Path.Combine(outside, ".txc")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task OperationGetCommand_RefreshOption_ReachesTheEnvironment()
    {
        await ConnectionCommandTestHost.RunAsync(new ConnectorOperationGetCliCommand { Connector = Dataverse, Operation = "ListRecords" });
        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationGetCliCommand { Connector = Dataverse, Operation = "ListRecords" });
        await ConnectionCommandTestHost.RunAsync(new ConnectorOperationGetCliCommand { Connector = Dataverse, Operation = "ListRecords", Refresh = true });

        Assert.Equal(0, exit);
        Assert.Equal("snapshot", JsonDocument.Parse(output).RootElement.GetProperty("source").GetString());
        Assert.Equal(2, _host.Operations.Fetches.Count);
    }

    private static OperationDetail ListRecords(string operation) => new(
        Dataverse, Dataverse, operation == "listrecords" ? "ListRecords" : operation, "List rows", null, "OpenApiConnection", false,
        [
            new OperationParameter("entityName", "string", true, "Table name", null, null,
                new DynamicValuesRef("GetEntitySets", new Dictionary<string, string?>(), "value", "EntitySetName", "DisplayName"), null, null,
                new Dictionary<string, string> { ["3"] = "Modified" }),
        ],
        Outputs:
        [
            new OperationParameter("body/value", "array", false, "List of Items", null, null, null, null,
                new DynamicValuesRef("GetMetadataForGetEntity",
                    new Dictionary<string, string?> { ["entityName"] = "$ref:entityName", ["selectedEntityAttributes"] = "$ref?:$select" }, ValuePath: "schema")),
        ]);
}
