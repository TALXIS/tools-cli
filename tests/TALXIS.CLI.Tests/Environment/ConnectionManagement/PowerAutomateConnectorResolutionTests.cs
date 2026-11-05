using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

/// <summary>Connector get, operation get and operation search accept what connector list prints.</summary>
public class PowerAutomateConnectorResolutionTests
{
    private static readonly HttpMethod Get = HttpMethod.Get;

    private static readonly IReadOnlyDictionary<string, string> LogicalNames = new Dictionary<string, string>
    {
        [ConnectionFixtures.CustomLogicalName] = ConnectionFixtures.CustomApiId,
    };

    private static string ConnectorPayload() => """
        {"name":"@@API@@","properties":{"displayName":"Open Food Facts","swagger":{"paths":{"/product":{"get":{"operationId":"GetProduct","summary":"Get a product"}}}}}}
        """.Replace("@@API@@", ConnectionFixtures.CustomApiId, StringComparison.Ordinal);

    private static FakePlatform Platform()
        => new FakePlatform()
            .On(Get, "connectivity/connectors?", HttpStatusCode.OK, ConnectionFixtures.Catalog())
            .On(Get, $"powerautomate/apis/{ConnectionFixtures.CustomApiId}?", HttpStatusCode.OK, ConnectorPayload())
            .On(Get, $"powerautomate/apis/{ConnectionFixtures.CustomApiId}/apiOperations/GetProduct?", HttpStatusCode.OK,
                """{"properties":{"summary":"Get a product"}}""")
            .On(HttpMethod.Post, "powerautomate/operations?", HttpStatusCode.OK, """{"value":[]}""");

    private static IConnectorMetadataService Service(FakePlatform platform)
        => ConnectionFixtures.Provider(platform, new FakeTokens(), ConnectionFixtures.User(), new FakeConnectorQuery(LogicalNames))
            .GetRequiredService<IConnectorMetadataService>();

    [Theory]
    [InlineData(ConnectionFixtures.CustomLogicalName)]
    [InlineData(ConnectionFixtures.CustomApiId)]
    [InlineData("open food facts")]
    public async Task GetConnector_AcceptsLogicalNameApiIdOrDisplayName(string connector)
    {
        var detail = await Service(Platform()).GetConnectorAsync(null, connector, null, CancellationToken.None);

        Assert.Equal(ConnectionFixtures.CustomApiId, detail.ApiId);
        Assert.Equal("Open Food Facts", detail.DisplayName);
        Assert.Equal("GetProduct", Assert.Single(detail.Operations).OperationId);
    }

    [Fact]
    public async Task GetConnector_ByLogicalName_NamesTheConnectorByItsLogicalName()
    {
        var detail = await Service(Platform())
            .GetConnectorAsync(null, ConnectionFixtures.CustomLogicalName, null, CancellationToken.None);

        Assert.Equal(ConnectionFixtures.CustomLogicalName, detail.LogicalName);
    }

    [Fact]
    public async Task GetOperation_ByLogicalName_ReadsTheOperationOfTheEnvironmentsConnector()
    {
        var detail = await Service(Platform()).GetOperationAsync(
            null, ConnectionFixtures.CustomLogicalName, "GetProduct", CancellationToken.None);

        Assert.Equal(ConnectionFixtures.CustomLogicalName, detail.Connector);
        Assert.Equal(ConnectionFixtures.CustomApiId, detail.ApiId);
        Assert.Equal("Get a product", detail.Summary);
    }

    [Fact]
    public async Task SearchOperations_ScopesTheSearchToTheResolvedApiId()
    {
        var platform = Platform();

        await Service(platform).SearchOperationsAsync(
            null, "product", ConnectionFixtures.CustomLogicalName, "all", 20, CancellationToken.None);

        var search = Assert.Single(platform.Sent(HttpMethod.Post, "powerautomate/operations?"));
        Assert.Contains($"\"operationGroupName\":\"{ConnectionFixtures.CustomApiId}\"", search.Body);
    }

    [Fact]
    public async Task GetConnector_AnUnknownConnector_IsAConnectorNotFound()
        => await Assert.ThrowsAsync<ConnectorNotFoundException>(
            () => Service(Platform()).GetConnectorAsync(null, "nope", null, CancellationToken.None));
}
