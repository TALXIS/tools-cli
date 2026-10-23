using System.Net;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class ConnectorMetadataServiceTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task GetOperation_CallsThePerEnvironmentHostWithThePowerAppsAudience()
    {
        var fake = new Fake(_ => Json(HttpStatusCode.OK, """{ "properties": { "inputsDefinition": {} } }"""));

        await fake.Service.GetOperationAsync(null, "shared_office365", "SendEmailV2", CancellationToken.None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("111111112222333344445555555555.55.environment.api.powerplatform.com", request.Uri.Host);
        Assert.Equal("/powerautomate/apis/shared_office365/apiOperations/SendEmailV2", request.Uri.AbsolutePath);
        Assert.Equal("Bearer token", request.Authorization);
        Assert.Equal(new Uri("https://service.powerapps.com/"), Assert.Single(fake.Audiences));
    }

    [Fact]
    public async Task Unauthorized_FailsWithoutTryingAnotherAudience()
    {
        // The audience is pinned: a 401 is a real permission problem to report,
        // not a cue to probe other resources.
        var fake = new Fake(_ => Json(HttpStatusCode.Unauthorized, """{ "error": "nope" }"""));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.Service.GetConnectorAsync(null, "shared_teams", null, CancellationToken.None));

        Assert.Single(fake.Audiences);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task NotFound_IsReportedAsBadInput()
    {
        var fake = new Fake(_ => Json(HttpStatusCode.NotFound, """{ "error": "no such operation" }"""));

        await Assert.ThrowsAsync<ArgumentException>(
            () => fake.Service.GetOperationAsync(null, "shared_teams", "MadeUpOperation", CancellationToken.None));
    }

    [Fact]
    public async Task TransientFailure_IsRetriedWithTheSameToken()
    {
        var calls = 0;
        var fake = new Fake(_ => ++calls == 1
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : Json(HttpStatusCode.OK, """{ "value": [] }"""));

        await fake.Service.ListConnectorsAsync(null, null, CancellationToken.None);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Single(fake.Audiences);
    }

    [Fact]
    public async Task MissingEnvironmentId_IsResolvedThroughTheCatalogOnce()
    {
        var fake = new Fake(_ => Json(HttpStatusCode.OK, """{ "value": [] }"""), storeEnvironmentId: false);

        await fake.Service.ListConnectorsAsync(null, null, CancellationToken.None);

        Assert.Equal(1, fake.Catalog.Lookups);
        Assert.StartsWith("111111112222333344445555555555.55.", Assert.Single(fake.Requests).Uri.Host);
    }

    [Fact]
    public async Task ListConnectors_FiltersByNameOrDisplayName()
    {
        var fake = new Fake(_ => Json(HttpStatusCode.OK, """
            { "value": [
              { "name": "shared_teams", "properties": { "displayName": "Microsoft Teams", "tier": "Standard" } },
              { "name": "shared_office365", "properties": { "displayName": "Office 365 Outlook" } }
            ] }
            """));

        var connectors = await fake.Service.ListConnectorsAsync(null, "outlook", CancellationToken.None);

        Assert.Equal("shared_office365", Assert.Single(connectors).Name);
    }

    [Fact]
    public async Task SearchOperations_SendsTheKindFilterAsTags()
    {
        var fake = new Fake(_ => Json(HttpStatusCode.OK, """{ "value": [] }"""));

        await fake.Service.SearchOperationsAsync(null, "send email", "shared_office365", "actions", 10, CancellationToken.None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("send email", body.RootElement.GetProperty("searchText").GetString());
        Assert.Equal("shared_office365", body.RootElement.GetProperty("operationGroupName").GetString());
        Assert.Equal("Action", body.RootElement.GetProperty("allTagsToInclude")[0].GetString());
        Assert.Equal(["Deprecated", "Trigger"],
            body.RootElement.GetProperty("anyTagsToExclude").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task SearchOperations_RejectsAnUnknownKindBeforeCallingTheEnvironment()
    {
        var fake = new Fake(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => fake.Service.SearchOperationsAsync(null, "x", null, "webhooks", 10, CancellationToken.None));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void SearchHits_DropOperationsOnlyMarkedDeprecatedInTheirSummary()
    {
        // Seen live: the Deprecated tag filter lets these through.
        using var document = JsonDocument.Parse("""
            { "value": [
              { "name": "mcp_EmailsManagement", "properties": { "summary": "Emails management (deprecated)" } },
              { "name": "SendEmailV2", "properties": { "summary": "Send an email (V2)",
                "operationGroup": { "name": "shared_office365", "displayName": "Office 365 Outlook" } } }
            ] }
            """);

        var hit = Assert.Single(ConnectorMetadataService.ReadSearchHits(document.RootElement, top: 10));
        Assert.Equal("SendEmailV2", hit.Name);
        Assert.Equal("shared_office365", hit.ConnectorName);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

    private sealed class Fake : IAccessTokenService, IHttpClientFactoryWrapper, IConfigurationResolver
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        private readonly Connection _connection;

        public Fake(Func<HttpRequestMessage, HttpResponseMessage> handler, bool storeEnvironmentId = true)
        {
            _handler = handler;
            _connection = new Connection
            {
                Id = "conn",
                EnvironmentUrl = "https://example.crm4.dynamics.com/",
                EnvironmentId = storeEnvironmentId ? EnvironmentId : null,
                Cloud = CloudInstance.Public,
            };
            Service = new ConnectorMetadataService(this, Catalog, this, this);
        }

        public ConnectorMetadataService Service { get; }
        public FakeCatalog Catalog { get; } = new();
        public List<Uri> Audiences { get; } = new();
        public List<RecordedRequest> Requests { get; } = new();

        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
            => Task.FromResult(new ResolvedProfileContext(
                null, _connection, new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser }, ResolutionSource.Ephemeral));

        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
        {
            Audiences.Add(resourceUri);
            return Task.FromResult("token");
        }

        public HttpClient Create() => new(new Handler(this));

        private sealed class Handler(Fake owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                owner.Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
                return owner._handler(request);
            }
        }
    }

    private sealed class FakeCatalog : IPowerPlatformEnvironmentCatalog
    {
        public int Lookups { get; private set; }

        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
            Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
        {
            Lookups++;
            return Task.FromResult<PowerPlatformEnvironmentSummary?>(new PowerPlatformEnvironmentSummary(
                EnvironmentId, "Dev", environmentUrl, null, null, null));
        }
    }
}
