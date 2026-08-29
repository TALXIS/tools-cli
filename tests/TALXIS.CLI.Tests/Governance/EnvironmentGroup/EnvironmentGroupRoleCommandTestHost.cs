using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;

namespace TALXIS.CLI.Tests.Governance.EnvironmentGroup;

/// <summary>
/// Test host for <c>txc governance environment-group role</c> command
/// tests. Combines an in-memory fake <see cref="IPowerPlatformEnvironmentGroupClient"/>
/// and <see cref="IPowerPlatformEnvironmentGroupRoleClient"/> (no HTTP
/// involved) with real <see cref="SecurityRoleResolver"/>/<see cref="PowerPlatformRbacClient"/>
/// instances backed by a queued fake HTTP transport (mirrors
/// <c>SecurityCommandTestHost</c>) - since command support genuinely reuses
/// that Graph-backed principal/role resolution code.
/// </summary>
internal sealed class EnvironmentGroupRoleCommandTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public EnvironmentGroupRoleCommandTestHost(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        GroupClient = new GovernanceCommandTestHost.FakeEnvironmentGroupClient();
        RoleClient = new FakeEnvironmentGroupRoleClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfigurationResolver>(new FixedResolver(TestContext()));
        services.AddSingleton<IPowerPlatformEnvironmentGroupClient>(GroupClient);
        services.AddSingleton<IPowerPlatformEnvironmentGroupRoleClient>(RoleClient);
        services.AddSingleton(_ => CreateRbacClient(handlers));
        services.AddSingleton(_ => CreateResolver(handlers));

        _provider = services.BuildServiceProvider();
        TxcServices.Initialize(_provider);
    }

    public GovernanceCommandTestHost.FakeEnvironmentGroupClient GroupClient { get; }

    public FakeEnvironmentGroupRoleClient RoleClient { get; }

    public void Dispose()
    {
        TxcServices.Reset();
        _provider.Dispose();
    }

    public static HttpResponseMessage JsonResponse(string json)
        => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };

    public static string RoleDefinitionsPayload(Guid ownerRoleId, Guid readerRoleId) =>
        $$"""
        {
          "value": [
            { "roleDefinitionId": "{{ownerRoleId}}", "roleDefinitionName": "Owner", "assignableScopes": ["/tenants/*", "/environmentGroups/*"] },
            { "roleDefinitionId": "{{readerRoleId}}", "roleDefinitionName": "Reader", "assignableScopes": ["/tenants/*", "/environmentGroups/*"] }
          ]
        }
        """;

    public static string UserPayload(Guid userId, string upn, string displayName) =>
        $$"""
        {
          "value": [
            { "id": "{{userId}}", "userPrincipalName": "{{upn}}", "displayName": "{{displayName}}" }
          ]
        }
        """;

    private static ResolvedProfileContext TestContext() => new(
        new Profile { Id = "test", ConnectionRef = "conn", CredentialRef = "cred" },
        new Connection
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            TenantId = "tenant-id",
            EnvironmentType = EnvironmentType.Sandbox,
        },
        new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser },
        ResolutionSource.CommandLine);

    private static PowerPlatformRbacClient CreateRbacClient(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
        => new(new FakeAccessTokenService(), new FakeHttpClientFactoryWrapper(handlers));

    private static SecurityRoleResolver CreateResolver(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var http = new FakeHttpClientFactoryWrapper(handlers);
        var tokens = new FakeAccessTokenService();
        var graph = new MicrosoftGraphClient(tokens, http);
        var rbac = new PowerPlatformRbacRoleStrategy(new PowerPlatformRbacClient(tokens, http));
        var bap = new BapAdminApplicationRoleStrategy(new BapAdminApiClient(tokens, http));
        return new SecurityRoleResolver(graph, rbac, bap);
    }

    private sealed class FixedResolver(ResolvedProfileContext context) : IConfigurationResolver
    {
        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct) => Task.FromResult(context);
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
            => Task.FromResult("token");
    }

    private sealed class FakeHttpClientFactoryWrapper(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers) : IHttpClientFactoryWrapper
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = handlers;

        public HttpClient Create() => new(new FakeHttpMessageHandler(_handlers));
    }

    private sealed class FakeHttpMessageHandler(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = handlers;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_handlers.Count == 0)
                throw new InvalidOperationException("No HTTP handler configured for this request.");

            return Task.FromResult(_handlers.Dequeue()(request));
        }
    }

    /// <summary>
    /// In-memory fake for environment-group role assignments, keyed by
    /// environment group id. Mutation calls are recorded for assertions.
    /// </summary>
    internal sealed class FakeEnvironmentGroupRoleClient : IPowerPlatformEnvironmentGroupRoleClient
    {
        private readonly Dictionary<Guid, List<PowerPlatformEnvironmentGroupRoleAssignment>> _assignments = new();

        public List<(Guid GroupId, string RoleAssignmentId)> Removed { get; } = new();

        public void Seed(Guid environmentGroupId, PowerPlatformEnvironmentGroupRoleAssignment assignment)
        {
            if (!_assignments.TryGetValue(environmentGroupId, out var list))
                _assignments[environmentGroupId] = list = new();

            list.Add(assignment);
        }

        public Task<IReadOnlyList<PowerPlatformEnvironmentGroupRoleAssignment>> ListAsync(
            Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PowerPlatformEnvironmentGroupRoleAssignment>>(
                _assignments.TryGetValue(environmentGroupId, out var list) ? list.ToList() : Array.Empty<PowerPlatformEnvironmentGroupRoleAssignment>());

        public Task<PowerPlatformEnvironmentGroupRoleAssignment> AddAsync(
            Connection connection, Credential credential, Guid environmentGroupId,
            PowerPlatformPrincipalType principalType, Guid principalObjectId, Guid roleDefinitionId, CancellationToken ct)
        {
            var assignment = new PowerPlatformEnvironmentGroupRoleAssignment(
                Guid.NewGuid().ToString(), environmentGroupId, principalType, principalObjectId, roleDefinitionId, DateTimeOffset.UtcNow, null);
            Seed(environmentGroupId, assignment);
            return Task.FromResult(assignment);
        }

        public Task RemoveAsync(Connection connection, Credential credential, Guid environmentGroupId, string roleAssignmentId, CancellationToken ct)
        {
            Removed.Add((environmentGroupId, roleAssignmentId));
            if (_assignments.TryGetValue(environmentGroupId, out var list))
                list.RemoveAll(a => a.RoleAssignmentId == roleAssignmentId);

            return Task.CompletedTask;
        }
    }
}
