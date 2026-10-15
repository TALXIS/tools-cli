using System.Net;
using System.Text.Json;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectorListServiceTests
{
    private static readonly HttpMethod Get = HttpMethod.Get;

    private static ConnectorResolutionService Service(IDataverseQueryService query, FakePlatform? platform = null)
    {
        platform ??= new FakePlatform().On(Get, "connectivity/connectors?", HttpStatusCode.OK, ConnectionFixtures.Catalog());
        return new ConnectorResolutionService(
            new FakeConfigurationResolver(ConnectionFixtures.Connection(), ConnectionFixtures.User()),
            new UnusedCatalog(),
            query,
            new FakeTokens(),
            platform.Http);
    }

    private sealed class RowQuery(params string[] rows) : IDataverseQueryService
    {
        public Task<DataverseQueryResult> QueryODataAsync(
            string? profileName, string entitySetOrPath, string? select, string? filter, string? orderBy, int? top, bool includeAnnotations, CancellationToken ct)
        {
            var records = rows.Select(r => JsonDocument.Parse(r).RootElement.Clone()).ToList();
            return Task.FromResult(new DataverseQueryResult(records, null));
        }

        public Task<DataverseQueryResult> QuerySqlAsync(string? profileName, string sql, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DataverseQueryResult> QueryFetchXmlAsync(string? profileName, string fetchXml, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FailingQuery : IDataverseQueryService
    {
        public Task<DataverseQueryResult> QueryODataAsync(
            string? profileName, string entitySetOrPath, string? select, string? filter, string? orderBy, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new InvalidOperationException("no dataverse access");

        public Task<DataverseQueryResult> QuerySqlAsync(string? profileName, string sql, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DataverseQueryResult> QueryFetchXmlAsync(string? profileName, string fetchXml, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static string CustomRow() => "{\"connectorid\":\"a01c544e-0ace-3b06-93ac-d1708abb3e94\",\"name\":\""
        + ConnectionFixtures.CustomLogicalName + "\",\"connectorinternalid\":\"" + ConnectionFixtures.CustomApiId + "\"}";

    [Fact]
    public async Task List_PairsACustomConnectorWithItsLogicalName_AndKeepsBuiltInsByApiId()
    {
        var service = Service(new RowQuery(CustomRow()));

        var items = await service.ListAsync(null, null, null, null, CancellationToken.None);

        var custom = Assert.Single(items, i => i.Kind == ConnectorKind.Custom);
        Assert.Equal(ConnectionFixtures.CustomLogicalName, custom.LogicalName);
        Assert.Equal(ConnectionFixtures.CustomApiId, custom.ApiId);
        Assert.Equal("Open Food Facts", custom.DisplayName);

        var builtIn = items.Single(i => i.ApiId == "shared_office365users");
        Assert.Equal(ConnectorKind.BuiltIn, builtIn.Kind);
        Assert.Equal("shared_office365users", builtIn.LogicalName);
    }

    [Fact]
    public async Task List_FiltersByLogicalNameApiIdOrDisplayName_AndByKind()
    {
        var service = Service(new RowQuery(CustomRow()));

        Assert.Equal(
            ConnectionFixtures.CustomLogicalName,
            Assert.Single(await service.ListAsync(null, "FOOD", null, null, CancellationToken.None)).LogicalName);
        Assert.Single(await service.ListAsync(null, "office 365", null, null, CancellationToken.None));
        Assert.Single(await service.ListAsync(null, null, ConnectorKind.Custom, null, CancellationToken.None));
        Assert.Equal(2, (await service.ListAsync(null, null, ConnectorKind.BuiltIn, null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task List_TopLimitsTheResult_AfterSorting()
    {
        var service = Service(new RowQuery(CustomRow()));

        var items = await service.ListAsync(null, null, null, 1, CancellationToken.None);

        Assert.Equal(ConnectionFixtures.CustomLogicalName, Assert.Single(items).LogicalName);
    }

    [Fact]
    public async Task List_UnknownKind_IsAValidationError()
    {
        var service = Service(new RowQuery());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ListAsync(null, null, "weird", null, CancellationToken.None));

        Assert.Contains("built-in", ex.Message);
    }

    [Fact]
    public async Task List_WithoutDataverseAccess_FallsBackToTheApiIdAsTheLogicalName()
    {
        var service = Service(new FailingQuery());

        var custom = Assert.Single(await service.ListAsync(null, null, ConnectorKind.Custom, null, CancellationToken.None));

        Assert.Equal(ConnectionFixtures.CustomApiId, custom.LogicalName);
    }

    [Fact]
    public async Task Resolve_ReturnsTheConnectorRowIdOfACustomConnector()
    {
        var service = Service(new FakeConnectorQuery(new Dictionary<string, string>
        {
            [ConnectionFixtures.CustomLogicalName] = ConnectionFixtures.CustomApiId,
        }));

        var resolved = await ((IConnectorResolver)service).ResolveAsync(null, ConnectionFixtures.CustomLogicalName, CancellationToken.None);

        Assert.True(resolved.IsCustom);
        Assert.Equal(ConnectionFixtures.CustomLogicalName, resolved.LogicalName);
        Assert.Equal(ConnectionFixtures.CustomApiId, resolved.ApiId);
        Assert.Equal(new Guid("a01c544e-0ace-3b06-93ac-d1708abb3e94"), resolved.ConnectorRowId);
    }
}
