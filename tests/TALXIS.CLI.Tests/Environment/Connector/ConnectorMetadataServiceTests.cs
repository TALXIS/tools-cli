using System.Net;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
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

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => fake.Service.GetOperationAsync(null, "shared_teams", "MadeUpOperation", CancellationToken.None));

        Assert.Contains("environment connector get shared_teams", ex.Message);
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

        await fake.Service.SearchOperationsAsync(null, "send email", "shared_office365", "action", 10, CancellationToken.None);

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

    private const string Runtime = "https://env.common.europe002.azure-apihub.net/apim/teams";
    private const string ConnectionId = "shared-teams-123";

    /// <summary>
    /// A Teams-shaped connector: location values come from a GET lookup that
    /// depends on poster, the body schema from a GET lookup that depends on
    /// both, and one value list from a DELETE lookup that must never be called.
    /// </summary>
    private static Fake TeamsFake() => new(request =>
    {
        var uri = request.RequestUri!.ToString();
        if (uri.StartsWith(Runtime + "/" + ConnectionId + "/flowbot/messageType/", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """{ "value": [ { "id": "Channel" }, { "id": "Chat with Flow bot" } ] }""");
        if (uri.StartsWith(Runtime + "/" + ConnectionId + "/flowbot/actions/", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """
                { "schema": { "type": "object", "required": [ "recipient", "messageBody" ], "properties": {
                  "recipient": { "type": "string", "title": "Recipient" },
                  "messageBody": { "type": "string", "format": "html" } } } }
                """);
        if (uri.Contains("/apiOperations/", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """
                { "properties": { "inputsDefinition": { "required": [ "poster", "location", "body" ], "properties": {
                  "poster": { "type": "string", "enum": [ "Flow bot", "User" ] },
                  "location": { "type": "string", "x-ms-dynamic-list": { "operationId": "GetMessageLocations",
                    "itemsPath": "value", "itemValuePath": "id",
                    "parameters": { "messageType": { "value": "ParentMessage" }, "poster": { "parameterReference": "poster" } } } },
                  "body": { "type": "object", "x-ms-dynamic-properties": { "operationId": "GetUnifiedActionSchema", "itemValuePath": "schema",
                    "parameters": { "actionType": { "value": "Message" }, "poster": { "parameterReference": "poster" },
                                    "recipientType": { "parameterReference": "location" } } } },
                  "tag": { "type": "string", "x-ms-dynamic-list": { "operationId": "SearchTags", "parameters": {} } }
                } } } }
                """);
        if (uri.Contains("api.powerapps.com", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """
                { "properties": { "displayName": "matej@contoso.com", "statuses": [ { "status": "Connected" } ],
                  "createdBy": { "displayName": "Matej", "userPrincipalName": "matej@contoso.com" } } }
                """);
        if (uri.Contains("/powerautomate/apis/shared_teams?", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, $$"""
                { "name": "shared_teams", "properties": { "primaryRuntimeUrl": "{{Runtime}}", "swagger": {
                  "parameters": { "poster": { "name": "poster", "in": "path", "required": true } },
                  "paths": {
                    "/{connectionId}/flowbot/messageType/{messageType}/poster/{poster}": { "get": { "operationId": "GetMessageLocations",
                      "parameters": [ { "name": "connectionId", "in": "path" }, { "name": "messageType", "in": "path" }, { "$ref": "#/parameters/poster" } ] } },
                    "/{connectionId}/flowbot/actions/{actionType}/posters/{poster}/recipienttypes/{recipientType}/schema": { "get": { "operationId": "GetUnifiedActionSchema",
                      "parameters": [ { "name": "connectionId", "in": "path" }, { "name": "actionType", "in": "path" },
                                      { "name": "poster", "in": "path" }, { "name": "recipientType", "in": "path" } ] } },
                    "/{connectionId}/tags/search": { "delete": { "operationId": "SearchTags" } }
                  } } } }
                """);
        return Json(HttpStatusCode.NotFound, "{}");
    });

    private static Task<TALXIS.CLI.Core.Platforms.PowerPlatform.OperationResolution> Resolve(
        Fake fake, string parameter, bool useConnection, params (string Name, string Value)[] values)
        => fake.Service.ResolveOperationAsync(
            null, "shared_teams", "PostMessageToConversation", "mtj_sharedteams", ConnectionId,
            values.ToDictionary(v => v.Name, v => v.Value), parameter, null, useConnection, CancellationToken.None);

    [Fact]
    public async Task Resolve_WithoutConsent_CallsNothingThroughTheConnection()
    {
        var fake = TeamsFake();

        var plan = await Resolve(fake, "location", useConnection: false, ("poster", "Flow bot"));

        Assert.False(plan.ConnectionUsed);
        Assert.Equal("Matej", plan.Connection.OwnerName);
        Assert.Equal("Connected", plan.Connection.Status);
        Assert.Equal("planned", Assert.Single(plan.Lookups).Status);

        // body also needs the location, which has not been chosen yet.
        var body = Assert.Single((await Resolve(fake, "body", useConnection: false, ("poster", "Flow bot"))).Lookups);
        Assert.Equal("skipped", body.Status);
        Assert.Contains("'location'", body.Reason);

        Assert.DoesNotContain(fake.Requests, r => r.Uri.ToString().StartsWith(Runtime, StringComparison.Ordinal));
        Assert.DoesNotContain(fake.Audiences, ApiHubAudiences.IsApiHub);
    }

    [Fact]
    public async Task Resolve_WithConsent_ExpandsTheBodyAndListsTheLocations()
    {
        var fake = TeamsFake();

        var locations = await Resolve(fake, "location", useConnection: true, ("poster", "Flow bot"));

        Assert.True(locations.ConnectionUsed);
        Assert.Contains(new Uri(ApiHubAudiences.Public), fake.Audiences);
        Assert.Contains(fake.Requests, r => r.Uri.AbsoluteUri == Runtime + "/" + ConnectionId + "/flowbot/messageType/ParentMessage/poster/Flow%20bot");
        Assert.Equal(["Channel", "Chat with Flow bot"],
            Assert.Single(locations.Operation.Parameters, p => p.Name == "location").AllowedValues);

        var body = await Resolve(fake, "body", useConnection: true, ("poster", "Flow bot"), ("location", "Chat with Flow bot"));
        var names = body.Operation.Parameters.Select(p => p.Name).ToList();
        Assert.Contains("body/recipient", names);
        Assert.Contains("body/messageBody", names);
        Assert.DoesNotContain("body", names);
        Assert.True(Assert.Single(body.Operation.Parameters, p => p.Name == "body/recipient").Required);
        Assert.Equal("resolved", Assert.Single(body.Lookups).Status);
    }

    [Fact]
    public async Task Resolve_AFailedLookupFailsInsteadOfReportingAStatus()
    {
        var teams = TeamsFake();
        var fake = new Fake(request => request.RequestUri!.ToString().StartsWith(Runtime, StringComparison.Ordinal)
            ? Json(HttpStatusCode.Unauthorized, """{ "error": "connection expired" }""")
            : teams.Respond(request));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Resolve(fake, "location", useConnection: true, ("poster", "Flow bot")));

        Assert.Contains("connection expired", ex.Message);
    }

    [Fact]
    public async Task Resolve_NeverSendsAWritingMethodThroughTheConnection()
    {
        var fake = TeamsFake();

        var result = await Resolve(fake, "tag", useConnection: true, ("poster", "Flow bot"), ("location", "Channel"));

        var tag = Assert.Single(result.Lookups);
        Assert.Equal("skipped", tag.Status);
        Assert.Contains("DELETE", tag.Reason);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    private const string DataverseRuntime = "https://env.common.europe002.azure-apihub.net/apim/commondataserviceforapps";

    /// <summary>
    /// Dataverse-shaped connector, as verified live: the current-environment
    /// operation's own lookups cannot be called directly, its WithOrganization
    /// twin declares the environment as a header, some lookups are POSTs with
    /// a body, and the response is camelCase while the metadata paths are PascalCase.
    /// </summary>
    private static Fake DataverseFake() => new(request =>
    {
        var uri = request.RequestUri!.ToString();
        if (uri.StartsWith(DataverseRuntime + "/" + ConnectionId + "/v1.0/$metadata.json/GetEntityListEnum/GetScopeFilters", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """{ "value": [ { "value": 1, "displayName": "User" }, { "value": 4, "displayName": "Organization" } ] }""");
        if (uri.Contains("/apiOperations/SubscribeWebhookTriggerWithOrganization", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """
                { "properties": { "inputsDefinition": { "properties": {
                  "organization": { "type": "string", "x-ms-dynamic-list": { "operationId": "GetOrganizations", "parameters": {} } },
                  "subscriptionRequest": { "type": "object", "required": [ "scope" ], "properties": {
                    "entityname": { "type": "string" },
                    "scope": { "type": "integer", "x-ms-dynamic-list": { "operationId": "GetScopeFiltersWithOrganization",
                      "itemsPath": "value", "itemValuePath": "Value", "itemTitlePath": "DisplayName",
                      "parameters": { "organization": { "parameterReference": "organization" },
                                      "body/entityName": { "parameterReference": "subscriptionRequest/entityname" } } } } } }
                } } } }
                """);
        if (uri.Contains("/apiOperations/ListRecords?", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """
                { "properties": { "inputsDefinition": { "properties": {
                  "entityName": { "type": "string", "x-ms-dynamic-list": { "operationId": "GetEntitySets", "parameters": {} } }
                } } } }
                """);
        if (uri.Contains("/apiOperations/SubscribeWebhookTrigger", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """
                { "properties": { "inputsDefinition": { "properties": {
                  "subscriptionRequest": { "type": "object", "required": [ "scope" ], "properties": {
                    "entityname": { "type": "string" },
                    "scope": { "type": "integer", "x-ms-dynamic-list": { "operationId": "GetScopeFilters",
                      "parameters": { "entityName": { "parameterReference": "subscriptionRequest/entityname" } } } } } }
                } } } }
                """);
        if (uri.Contains("api.powerapps.com", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, """{ "properties": { "statuses": [ { "status": "Connected" } ] } }""");
        if (uri.Contains("/powerautomate/apis/shared_commondataserviceforapps?", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, $$"""
                { "properties": { "primaryRuntimeUrl": "{{DataverseRuntime}}", "swagger": { "paths": {
                  "/{connectionId}/entities/{entityName}/scopefilters": { "get": { "operationId": "GetScopeFilters",
                    "parameters": [ { "name": "connectionId", "in": "path" }, { "name": "entityName", "in": "path", "required": true } ] } },
                  "/{connectionId}/v1.0/$metadata.json/GetEntityListEnum/GetScopeFiltersWithOrganization": { "post": { "operationId": "GetScopeFiltersWithOrganization",
                    "parameters": [ { "name": "connectionId", "in": "path" }, { "name": "organization", "in": "header", "required": true },
                                    { "name": "body", "in": "body", "required": true } ] } }
                } } } }
                """);
        return Json(HttpStatusCode.NotFound, "{}");
    });

    [Fact]
    public async Task Resolve_Dataverse_UsesTheTwinWhoseLookupsDeclareTheEnvironment()
    {
        var fake = DataverseFake();

        var result = await fake.Service.ResolveOperationAsync(
            null, "shared_commondataserviceforapps", "SubscribeWebhookTrigger", "mtj_dataverse", ConnectionId,
            new Dictionary<string, string> { ["subscriptionRequest/entityname"] = "account" }, "subscriptionRequest/scope", null, useConnection: true, CancellationToken.None);

        var lookup = Assert.Single(result.Lookups);
        Assert.Equal("GetScopeFiltersWithOrganization", lookup.OperationId);
        Assert.Equal("resolved", lookup.Status);

        // A declared POST, with the environment as its declared header and the
        // body/... arguments nested into the JSON body.
        var call = Assert.Single(fake.Requests, r => r.Uri.ToString().StartsWith(DataverseRuntime, StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://example.crm4.dynamics.com", call.Headers["organization"]);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal("account", body.RootElement.GetProperty("entityName").GetString());

        // Answers land on the operation that was asked about, which has no
        // organization parameter of its own. PascalCase paths match camelCase JSON.
        Assert.Equal("SubscribeWebhookTrigger", result.Operation.OperationId);
        Assert.DoesNotContain(result.Operation.Parameters, p => p.Name == "organization");
        var scope = Assert.Single(result.Operation.Parameters, p => p.Name == "subscriptionRequest/scope");
        Assert.Equal(["1", "4"], scope.AllowedValues);
        Assert.Equal("Organization", scope.ValueLabels!["4"]);
    }

    [Fact]
    public async Task Resolve_Dataverse_WithoutATwin_SaysSoInsteadOfPlanningLookupsThatCannotAnswer()
    {
        var fake = DataverseFake();

        var result = await fake.Service.ResolveOperationAsync(
            null, "shared_commondataserviceforapps", "ListRecords", "mtj_dataverse", ConnectionId,
            new Dictionary<string, string>(), "entityName", null, useConnection: true, CancellationToken.None);

        var lookup = Assert.Single(result.Lookups);
        Assert.Equal("skipped", lookup.Status);
        Assert.Contains("ListRecordsWithOrganization", lookup.Reason);
        Assert.False(result.ConnectionUsed);
        Assert.DoesNotContain(fake.Requests, r => r.Uri.ToString().StartsWith(DataverseRuntime, StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed record RecordedRequest(
        HttpMethod Method, Uri Uri, string? Authorization, string? Body, IReadOnlyDictionary<string, string> Headers);

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

        public HttpResponseMessage Respond(HttpRequestMessage request) => _handler(request);
        public FakeCatalog Catalog { get; } = new();
        public List<Uri> Audiences { get; } = new();
        public List<RecordedRequest> Requests { get; } = new();

        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
            => Task.FromResult(new ResolvedProfileContext(
                null, _connection, new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser }, ResolutionSource.Ephemeral));

        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
        {
            Audiences.Add(resourceUri);
            return Task.FromResult(ApiHubAudiences.IsApiHub(resourceUri) ? "runtime-token" : "token");
        }

        public HttpClient Create() => new(new Handler(this));

        private sealed class Handler(Fake owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
                owner.Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, headers));
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
