using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectorCatalogServiceTests
{
    private static readonly HttpMethod Get = HttpMethod.Get;

    private static ServiceProvider Provider(
        IDataverseQueryService query, FakePlatform? platform = null, TimeProvider? clock = null, TimeSpan? lifetime = null)
    {
        platform ??= new FakePlatform().On(Get, "connectivity/connectors?", HttpStatusCode.OK, ConnectionFixtures.Catalog());
        return ConnectionFixtures.Provider(platform, new FakeTokens(), ConnectionFixtures.User(), query, clock, lifetime);
    }

    private static IConnectorService Service(IDataverseQueryService query)
        => Provider(query).GetRequiredService<IConnectorService>();

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

    /// <summary>Dataverse cannot be reached at all: the query service fails before any query runs.</summary>
    private sealed class UnauthenticatedQuery : IDataverseQueryService
    {
        public Task<DataverseQueryResult> QueryODataAsync(
            string? profileName, string entitySetOrPath, string? select, string? filter, string? orderBy, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new InvalidOperationException("Run txc config auth login.");

        public Task<DataverseQueryResult> QuerySqlAsync(string? profileName, string sql, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DataverseQueryResult> QueryFetchXmlAsync(string? profileName, string fetchXml, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FailingQuery : IDataverseQueryService
    {
        public Task<DataverseQueryResult> QueryODataAsync(
            string? profileName, string entitySetOrPath, string? select, string? filter, string? orderBy, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new DataverseQueryException("the connector table is not available", new InvalidOperationException());

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

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
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
        var resolver = Provider(new FakeConnectorQuery(new Dictionary<string, string>
        {
            [ConnectionFixtures.CustomLogicalName] = ConnectionFixtures.CustomApiId,
        })).GetRequiredService<IConnectorResolver>();

        var resolved = await resolver.ResolveAsync(null, ConnectionFixtures.CustomLogicalName, CancellationToken.None);

        Assert.True(resolved.IsCustom);
        Assert.Equal(ConnectionFixtures.CustomLogicalName, resolved.LogicalName);
        Assert.Equal(ConnectionFixtures.CustomApiId, resolved.ApiId);
        Assert.Equal(new Guid("a01c544e-0ace-3b06-93ac-d1708abb3e94"), resolved.ConnectorRowId);
    }

    [Fact]
    public async Task List_WhenDataverseCannotBeReached_SurfacesTheFailureInsteadOfHidingIt()
    {
        var service = Service(new UnauthenticatedQuery());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ListAsync(null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_WhenDataverseCannotBeReached_SurfacesTheFailure()
    {
        var resolver = Provider(new UnauthenticatedQuery()).GetRequiredService<IConnectorResolver>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(null, ConnectionFixtures.CustomLogicalName, CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_AnApiIdNeverAsksDataverse()
    {
        var resolver = Provider(new UnauthenticatedQuery()).GetRequiredService<IConnectorResolver>();

        var resolved = await resolver.ResolveAsync(null, "/providers/Microsoft.PowerApps/apis/shared_office365users", CancellationToken.None);

        Assert.Equal("shared_office365users", resolved.ApiId);
        Assert.False(resolved.IsCustom);
    }

    [Fact]
    public async Task Resolve_UnknownName_IsAConnectorNotFound()
    {
        var resolver = Provider(new RowQuery()).GetRequiredService<IConnectorResolver>();

        var ex = await Assert.ThrowsAsync<ConnectorNotFoundException>(
            () => resolver.ResolveAsync(null, "nope", CancellationToken.None));

        Assert.Equal("nope", ex.Connector);
    }

    [Fact]
    public async Task Resolve_AnExactDisplayName_FindsTheConnector_WhenNoLogicalNameMatches()
    {
        var resolver = Provider(new RowQuery()).GetRequiredService<IConnectorResolver>();

        var resolved = await resolver.ResolveAsync(null, "office 365 users", CancellationToken.None);

        Assert.Equal("shared_office365users", resolved.ApiId);
        Assert.Equal("Office 365 Users", resolved.DisplayName);
    }

    [Fact]
    public async Task Resolve_ALogicalNameDataverseKnowsButTheCatalogDoesNot_IsACustomConnectorWithoutARow()
    {
        var resolver = Provider(new FakeConnectorQuery(new Dictionary<string, string>
        {
            ["almlab_notyetlisted"] = "shared_almlab-5fnotyetlisted",
        })).GetRequiredService<IConnectorResolver>();

        var resolved = await resolver.ResolveAsync(null, "almlab_notyetlisted", CancellationToken.None);

        Assert.True(resolved.IsCustom);
        Assert.Equal("shared_almlab-5fnotyetlisted", resolved.ApiId);
        Assert.Equal("almlab_notyetlisted", resolved.LogicalName);
        Assert.Null(resolved.ConnectorRowId);
    }

    [Fact]
    public async Task Resolve_AnApiIdWinsOverALogicalNameLookup()
    {
        // Were the logical name consulted first, this query would redirect the apiId elsewhere.
        var resolver = Provider(new FakeConnectorQuery(new Dictionary<string, string>
        {
            ["shared_office365users"] = "shared_something-5felse",
        })).GetRequiredService<IConnectorResolver>();

        var resolved = await resolver.ResolveAsync(null, "shared_office365users", CancellationToken.None);

        Assert.Equal("shared_office365users", resolved.ApiId);
    }

    [Fact]
    public async Task Resolve_ADisplayNameSharedByTwoConnectors_ListsTheCandidates()
    {
        var catalog = """
            {"value":[
              {"name":"shared_a","properties":{"displayName":"Same Name"}},
              {"name":"shared_b","properties":{"displayName":"same name"}}]}
            """;
        var platform = new FakePlatform().On(Get, "connectivity/connectors?", HttpStatusCode.OK, catalog);
        var resolver = Provider(new RowQuery(), platform).GetRequiredService<IConnectorResolver>();

        var ex = await Assert.ThrowsAsync<AmbiguousConnectorException>(
            () => resolver.ResolveAsync(null, "Same Name", CancellationToken.None));

        Assert.Equal(["shared_a", "shared_b"], ex.Candidates);
    }

    [Fact]
    public async Task Catalog_IsFetchedOnceWithinItsLifetime_AndAgainAfterIt()
    {
        var clock = new ManualTimeProvider();
        var platform = new FakePlatform().On(Get, "connectivity/connectors?", HttpStatusCode.OK, ConnectionFixtures.Catalog());
        using var provider = Provider(new RowQuery(), platform, clock, TimeSpan.FromMinutes(2));
        var service = provider.GetRequiredService<IConnectorService>();

        await service.ListAsync(null, null, null, null, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.ListAsync(null, null, null, null, CancellationToken.None);
        Assert.Single(platform.Sent(Get, "connectivity/connectors?"));

        clock.Advance(TimeSpan.FromMinutes(2));
        await service.ListAsync(null, null, null, null, CancellationToken.None);
        Assert.Equal(2, platform.Sent(Get, "connectivity/connectors?").Count());
    }

    [Fact]
    public async Task TheResolverAndTheListingShareOneCatalogCache()
    {
        var platform = new FakePlatform()
            .On(Get, "connectivity/connectors?", HttpStatusCode.OK, ConnectionFixtures.Catalog())
            .On(Get, "connectivity/connections?", HttpStatusCode.OK, "{\"value\":[]}");
        using var provider = Provider(new RowQuery(), platform);

        await provider.GetRequiredService<IConnectorService>().ListAsync(null, null, null, null, CancellationToken.None);
        await provider.GetRequiredService<IConnectorResolver>().ResolveAsync(null, "shared_office365users", CancellationToken.None);
        await provider.GetRequiredService<IConnectionService>().ListAsync(null, "shared_office365users", null, CancellationToken.None);

        Assert.Single(platform.Sent(Get, "connectivity/connectors?"));
    }
}
