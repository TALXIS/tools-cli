using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using TALXIS.CLI.Platform.PowerPlatform.Control.DependencyInjection;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

/// <summary>One request the fake platform saw.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, bool HadAuthorization);

/// <summary>
/// A scriptable stand-in for the connectivity API, the consent service and
/// Graph: requests are matched by method and a path fragment, and every
/// request is recorded so tests can assert on what was sent.
/// </summary>
internal sealed class FakePlatform
{
    private readonly List<(HttpMethod Method, string Fragment, Func<RecordedRequest, HttpResponseMessage> Respond)> _routes = new();

    public List<RecordedRequest> Requests { get; } = new();

    public IHttpClientFactoryWrapper Http => new Factory(this);

    public FakePlatform On(HttpMethod method, string pathFragment, HttpStatusCode status, string body = "")
        => On(method, pathFragment, _ => Response(status, body));

    public FakePlatform On(HttpMethod method, string pathFragment, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        // Later registrations win, so a test can override a default.
        _routes.Insert(0, (method, pathFragment, respond));
        return this;
    }

    public IEnumerable<RecordedRequest> Sent(HttpMethod method, string fragment)
        => Requests.Where(r => r.Method == method && r.Uri.ToString().Contains(fragment, StringComparison.Ordinal));

    public static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization is not null);
        Requests.Add(recorded);

        foreach (var (method, fragment, respond) in _routes)
        {
            if (method == request.Method && request.RequestUri!.ToString().Contains(fragment, StringComparison.Ordinal))
                return respond(recorded);
        }

        return Response(HttpStatusCode.NotFound,
            "{\"error\":{\"code\":\"NoRoute\",\"message\":\"" + request.Method + " " + request.RequestUri + "\"}}");
    }

    private sealed class Factory(FakePlatform owner) : IHttpClientFactoryWrapper
    {
        public HttpClient Create() => new(new Handler(owner));
    }

    private sealed class Handler(FakePlatform owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(owner.Handle(request));
    }
}

/// <summary>
/// Issues tokens shaped like real ones (a JWT with an <c>oid</c> claim) and
/// records which audience each was requested for.
/// </summary>
internal sealed class FakeTokens : IAccessTokenService
{
    public const string ObjectId = "0cf68951-a282-40e9-960c-a35ad855a60e";

    public List<Uri> Audiences { get; } = new();

    public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
    {
        Audiences.Add(resourceUri);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"oid\":\"" + ObjectId + "\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Task.FromResult($"header.{payload}.signature");
    }
}

internal sealed class FakeConfigurationResolver(Connection connection, Credential credential) : IConfigurationResolver
{
    public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
        => Task.FromResult(new ResolvedProfileContext(null, connection, credential, ResolutionSource.CommandLine));
}

/// <summary>The environment id is stored on the connection, so the catalog is never consulted.</summary>
internal sealed class UnusedCatalog : IPowerPlatformEnvironmentCatalog
{
    public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
        => throw new InvalidOperationException("The catalog should not be needed.");

    public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
        => throw new InvalidOperationException("The catalog should not be needed.");
}

/// <summary>Answers connector logical-name lookups from a fixed map.</summary>
internal sealed class FakeConnectorQuery(IReadOnlyDictionary<string, string>? logicalToApiId = null) : IDataverseQueryService
{
    public Task<DataverseQueryResult> QueryODataAsync(
        string? profileName, string entitySetOrPath, string? select, string? filter, string? orderBy, int? top, bool includeAnnotations, CancellationToken ct)
    {
        var records = new List<JsonElement>();
        if (entitySetOrPath == "connectors" && logicalToApiId is not null)
        {
            foreach (var (logical, apiId) in logicalToApiId)
            {
                if (filter is not null && filter.Contains($"'{logical}'", StringComparison.Ordinal))
                {
                    using var doc = JsonDocument.Parse("{\"name\":\"" + logical + "\",\"connectorinternalid\":\"" + apiId + "\"}");
                    records.Add(doc.RootElement.Clone());
                }
            }
        }
        return Task.FromResult(new DataverseQueryResult(records, null));
    }

    public Task<DataverseQueryResult> QuerySqlAsync(string? profileName, string sql, int? top, bool includeAnnotations, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<DataverseQueryResult> QueryFetchXmlAsync(string? profileName, string fetchXml, int? top, bool includeAnnotations, CancellationToken ct)
        => throw new NotSupportedException();
}

internal static class ConnectionFixtures
{
    public static readonly Guid EnvironmentId = new("72946668-bfcc-e9a8-9056-b023c2381946");

    public const string CustomApiId = "shared_almlab-5fopen-20food-20facts-5fa7d7fde51b746a91";
    public const string CustomLogicalName = "almlab_connectorsopenfoodfacts";

    public static Connection Connection() => new()
    {
        Id = "test",
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = "https://org.crm4.dynamics.com",
        EnvironmentId = EnvironmentId,
        Cloud = CloudInstance.Public,
        TenantId = "957a313a-971b-4add-b513-038b581bc3e7",
    };

    public static Credential User() => new() { Id = "user", Kind = CredentialKind.DeviceCode };

    public static Credential ServicePrincipal() => new() { Id = "spn", Kind = CredentialKind.ClientSecret };

    /// <summary>A catalog with one built-in and one custom connector.</summary>
    public static string Catalog() => Fill("""
        {"value":[
          {"name":"shared_office365users","properties":{"displayName":"Office 365 Users","isCustomApi":false}},
          {"name":"shared_commondataserviceforapps","properties":{"displayName":"Microsoft Dataverse","isCustomApi":false}},
          {"name":"@@API@@","properties":{"displayName":"Open Food Facts","isCustomApi":true,"xrmConnectorId":"a01c544e-0ace-3b06-93ac-d1708abb3e94"}}
        ]}
        """, ("API", CustomApiId));

    /// <summary>A custom connector that needs no authentication.</summary>
    public static string NoAuthConnector() => Fill("""
        {"name":"@@API@@","properties":{"displayName":"Open Food Facts","isCustomApi":true,"connectionParameters":{}}}
        """, ("API", CustomApiId));

    /// <summary>User-delegated OAuth only, as Office 365 Users.</summary>
    public static string OAuthConnector() => """
        {"name":"shared_office365users","properties":{"displayName":"Office 365 Users","connectionParameters":{
          "token":{"type":"oauthSetting","oAuthSettings":{"properties":{"IsOnbehalfofLoginSupported":true}}}}}}
        """;

    /// <summary>Three methods, as the Dataverse connector: OAuth, service principal, certificate.</summary>
    public static string DataverseConnector() => """
        {"name":"shared_commondataserviceforapps","properties":{"displayName":"Microsoft Dataverse","connectionParameterSets":{"values":[
          {"name":"Oauth","uiDefinition":{"displayName":"Oauth"},"parameters":{
            "token":{"type":"oauthSetting","uiDefinition":{"constraints":{"required":"true","hidden":"false"}}},
            "token:TenantId":{"type":"string","uiDefinition":{"constraints":{"required":"false","hidden":"true"}}}}},
          {"name":"ServicePrincipalOauth","uiDefinition":{"displayName":"Service Principal"},"parameters":{
            "token":{"type":"oauthSetting","uiDefinition":{"constraints":{"required":"true","hidden":"true"}}},
            "token:clientId":{"type":"string","uiDefinition":{"constraints":{"required":"true","hidden":"false"}}},
            "token:clientSecret":{"type":"securestring","uiDefinition":{"constraints":{"required":"true","hidden":"false"}}},
            "token:TenantId":{"type":"string","uiDefinition":{"constraints":{"required":"true","hidden":"false"}}}}},
          {"name":"CertOauth","uiDefinition":{"displayName":"Client Certificate Auth"},"parameters":{
            "token":{"type":"oauthSetting","uiDefinition":{"constraints":{"required":"false","hidden":"false"}}},
            "token:tenantId":{"type":"string","uiDefinition":{"constraints":{"required":"true","hidden":"false"}}},
            "token:clientCertificateSecret":{"type":"clientCertificate","uiDefinition":{"constraints":{"required":"true","hidden":"false"}}}}}
        ]}}}
        """;

    public static string Connected(string id, string apiId, string displayName) => Fill("""
        {"name":"@@ID@@","id":"/providers/Microsoft.PowerApps/apis/@@API@@/connections/@@ID@@","properties":{
          "apiId":"/providers/Microsoft.PowerApps/apis/@@API@@","displayName":"@@NAME@@","statuses":[{"status":"Connected"}],
          "createdTime":"2026-10-15T17:36:41.82Z","createdBy":{"type":"User","userPrincipalName":"admin@contoso.com"}}}
        """, ("ID", id), ("API", apiId), ("NAME", displayName));

    private static string Fill(string template, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
            template = template.Replace("@@" + key + "@@", value, StringComparison.Ordinal);
        return template;
    }

    public static string Wrap(params string[] connections) => "{\"value\":[" + string.Join(",", connections) + "]}";

    public static string Unauthenticated(string id, string apiId, bool withFirstParty) => Fill("""
        {"name":"@@ID@@","properties":{"apiId":"/providers/Microsoft.PowerApps/apis/@@API@@","displayName":"Mail",
          "statuses":[{"status":"Error","target":"Token","error":{"code":"Unauthenticated","message":"This connection is not authenticated."}}],
          "testLinks":[{"requestUri":"https://env.example/connectors/runtime/invoke/apis/office365users/connections/@@ID@@/testconnection","method":"get"}],
          "consentInfo":{"consentLink":"https://usa.consent.azure-apihub.net/login?data=abc"@@FIRST@@}}}
        """, ("ID", id), ("API", apiId),
        ("FIRST", withFirstParty ? ",\"firstPartyLoginUri\":\"https://usa.consent.azure-apihub.net/firstPartyLogin?data=abc\"" : ""));

    /// <summary>
    /// The connectivity services wired the way the host wires them, over the
    /// fake platform. <paramref name="clock"/> and <paramref name="catalogLifetime"/>
    /// control the connector catalog cache.
    /// </summary>
    public static ServiceProvider Provider(
        FakePlatform platform,
        FakeTokens tokens,
        Credential credential,
        IDataverseQueryService? query = null,
        TimeProvider? clock = null,
        TimeSpan? catalogLifetime = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfigurationResolver>(new FakeConfigurationResolver(Connection(), credential));
        services.AddSingleton<IPowerPlatformEnvironmentCatalog>(new UnusedCatalog());
        services.AddSingleton(query ?? new FakeConnectorQuery());
        services.AddSingleton<IAccessTokenService>(tokens);
        services.AddSingleton<IHttpClientFactoryWrapper>(platform.Http);
        services.AddSingleton(new MicrosoftGraphClient(tokens, platform.Http));
        if (clock is not null)
            services.AddSingleton(clock);
        if (catalogLifetime is { } lifetime)
            services.AddSingleton(new ConnectorCatalogOptions(lifetime));
        services.AddTxcPowerPlatformConnectivity();
        return services.BuildServiceProvider();
    }

    public static IConnectionService Service(
        FakePlatform platform, FakeTokens tokens, Credential credential, IReadOnlyDictionary<string, string>? logicalNames = null)
        => Provider(platform, tokens, credential, new FakeConnectorQuery(logicalNames)).GetRequiredService<IConnectionService>();

    public static IConnectionPermissionService PermissionService(
        FakePlatform platform, FakeTokens tokens, Credential credential)
        => Provider(platform, tokens, credential).GetRequiredService<IConnectionPermissionService>();

    /// <summary>A platform that serves the catalog, the connection list and one connector definition.</summary>
    public static FakePlatform Platform(string connectorApiId, string connectorDefinition, string existingConnections = "{\"value\":[]}")
        => new FakePlatform()
            .On(HttpMethod.Get, "connectivity/connectors?", HttpStatusCode.OK, Catalog())
            .On(HttpMethod.Get, "connectivity/connections?", HttpStatusCode.OK, existingConnections)
            .On(HttpMethod.Get, $"connectivity/connectors/{connectorApiId}?", HttpStatusCode.OK, connectorDefinition);
}

/// <summary>A clock tests advance by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
