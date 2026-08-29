using System.Diagnostics;
using System.Text.Json;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Telemetry;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class McpTelemetryEnricherTests
{
    [Fact]
    public async Task TagActivityAsync_WithExplicitProfile_DelegatesToIdentityTagger()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("execute_operation");

        var resolver = new FakeConfigurationResolver(_ => CreateResolvedProfileContext());
        var enricher = new McpTelemetryEnricher(new ActivityIdentityTagger(resolver));
        var arguments = new Dictionary<string, JsonElement>
        {
            ["profile"] = JsonDocument.Parse("\"custom-profile\"").RootElement.Clone()
        };

        await enricher.TagActivityAsync(activity, arguments, workingDirectory: null, CancellationToken.None);

        Assert.Single(resolver.RequestedProfiles);
        Assert.Equal("custom-profile", resolver.RequestedProfiles[0]);
        Assert.Equal("user@example.com", activity?.GetTagItem(TxcTelemetryTags.EndUserName));
    }

    [Fact]
    public async Task TagActivityAsync_WithExplicitProfile_FallsBackToStoresWhenTaggerMissing()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("execute_operation");

        var profile = new Profile { Id = "custom-profile", ConnectionRef = "conn", CredentialRef = "cred" };
        var connection = new Connection
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            DisplayName = "Contoso Sandbox",
            TenantId = "tenant-123"
        };
        var credential = new Credential
        {
            Id = "cred",
            Kind = CredentialKind.InteractiveBrowser,
            InteractiveUpn = "user@example.com"
        };

        var enricher = new McpTelemetryEnricher(
            tagger: null,
            profiles: new FakeProfileStore(profile),
            connections: new FakeConnectionStore(connection),
            credentials: new FakeCredentialStore(credential),
            globalConfig: new FakeGlobalConfigStore(new GlobalConfig()),
            workspaceDiscovery: new FakeWorkspaceDiscovery("workspace-profile"));
        var arguments = new Dictionary<string, JsonElement>
        {
            ["profile"] = JsonDocument.Parse("\"custom-profile\"").RootElement.Clone()
        };

        await enricher.TagActivityAsync(activity, arguments, workingDirectory: null, CancellationToken.None);

        Assert.Equal("user@example.com", activity?.GetTagItem(TxcTelemetryTags.EndUserName));
        Assert.Equal("tenant-123", activity?.GetTagItem(TxcTelemetryTags.EndUserScope));
    }

    [Fact]
    public async Task TagActivityAsync_WithoutExplicitProfile_UsesProvidedWorkingDirectoryForWorkspaceResolution()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("execute_operation");

        var profile = new Profile { Id = "workspace-profile", ConnectionRef = "conn", CredentialRef = "cred" };
        var connection = new Connection
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            DisplayName = "Contoso Sandbox",
            TenantId = "tenant-123"
        };
        var credential = new Credential
        {
            Id = "cred",
            Kind = CredentialKind.InteractiveBrowser,
            InteractiveUpn = "user@example.com"
        };

        var enricher = new McpTelemetryEnricher(
            tagger: null,
            profiles: new FakeProfileStore(profile),
            connections: new FakeConnectionStore(connection),
            credentials: new FakeCredentialStore(credential),
            globalConfig: new FakeGlobalConfigStore(new GlobalConfig()),
            workspaceDiscovery: new FakeWorkspaceDiscovery("workspace-profile"));

        await enricher.TagActivityAsync(
            activity,
            arguments: null,
            workingDirectory: "/tmp/client-root",
            CancellationToken.None);

        Assert.Equal("user@example.com", activity?.GetTagItem(TxcTelemetryTags.EndUserName));
    }

    [Theory]
    [InlineData("profile-a", "profile-a")]
    [InlineData(null, null)]
    public void TryGetProfile_ReadsProfileStringOrReturnsNull(string? jsonValue, string? expected)
    {
        IReadOnlyDictionary<string, JsonElement>? arguments = jsonValue is null
            ? null
            : new Dictionary<string, JsonElement>
            {
                ["profile"] = JsonDocument.Parse($"\"{jsonValue}\"").RootElement.Clone()
            };

        var result = McpTelemetryEnricher.TryGetProfile(arguments);

        Assert.Equal(expected, result);
    }

    private static ResolvedProfileContext CreateResolvedProfileContext()
    {
        return new ResolvedProfileContext(
            new Profile { Id = "test-profile", ConnectionRef = "conn", CredentialRef = "cred" },
            new Connection
            {
                Id = "conn",
                Provider = ProviderKind.Dataverse,
                EnvironmentUrl = "https://contoso.crm.dynamics.com",
                DisplayName = "Contoso Sandbox",
                TenantId = "tenant-123"
            },
            new Credential
            {
                Id = "cred",
                Kind = CredentialKind.InteractiveBrowser,
                InteractiveUpn = "user@example.com"
            },
            ResolutionSource.CommandLine);
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TxcActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class FakeConfigurationResolver : IConfigurationResolver
    {
        private readonly Func<string?, ResolvedProfileContext> _factory;

        public FakeConfigurationResolver(Func<string?, ResolvedProfileContext> factory)
        {
            _factory = factory;
        }

        public List<string?> RequestedProfiles { get; } = [];

        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
        {
            RequestedProfiles.Add(profileName);
            return Task.FromResult(_factory(profileName));
        }
    }

    private sealed class FakeProfileStore : IProfileStore
    {
        private readonly Profile _profile;

        public FakeProfileStore(Profile profile) => _profile = profile;

        public Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Profile>>([_profile]);

        public Task<Profile?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<Profile?>(string.Equals(id, _profile.Id, StringComparison.Ordinal) ? _profile : null);

        public Task UpsertAsync(Profile profile, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeConnectionStore : IConnectionStore
    {
        private readonly Connection _connection;

        public FakeConnectionStore(Connection connection) => _connection = connection;

        public Task<IReadOnlyList<Connection>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Connection>>([_connection]);

        public Task<Connection?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<Connection?>(string.Equals(id, _connection.Id, StringComparison.Ordinal) ? _connection : null);

        public Task UpsertAsync(Connection connection, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Credential _credential;

        public FakeCredentialStore(Credential credential) => _credential = credential;

        public Task<IReadOnlyList<Credential>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Credential>>([_credential]);

        public Task<Credential?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<Credential?>(string.Equals(id, _credential.Id, StringComparison.Ordinal) ? _credential : null);

        public Task UpsertAsync(Credential credential, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeGlobalConfigStore : IGlobalConfigStore
    {
        private readonly GlobalConfig _config;

        public FakeGlobalConfigStore(GlobalConfig config) => _config = config;

        public Task<GlobalConfig> LoadAsync(CancellationToken ct) => Task.FromResult(_config);

        public Task SaveAsync(GlobalConfig config, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeWorkspaceDiscovery : IWorkspaceDiscovery
    {
        private readonly string _defaultProfile;

        public FakeWorkspaceDiscovery(string defaultProfile)
        {
            _defaultProfile = defaultProfile;
        }

        public Task<WorkspaceResolution?> DiscoverAsync(string startDirectory, CancellationToken ct)
        {
            return Task.FromResult<WorkspaceResolution?>(
                new WorkspaceResolution(
                    startDirectory,
                    Path.Combine(startDirectory, ".txc", "workspace.json"),
                    new WorkspaceConfig { DefaultProfile = _defaultProfile }));
        }
    }
}
