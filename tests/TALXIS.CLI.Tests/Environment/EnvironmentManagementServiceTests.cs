using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Environment;

public sealed class EnvironmentManagementServiceTests
{
    [Fact]
    public async Task List_FallsBackToSingleAuthCredential_WhenNoProfileResolves()
    {
        var credentials = new InMemoryCredentialStore(new Credential
        {
            Id = "admin",
            Kind = CredentialKind.DeviceCode,
            TenantId = "tenant-id",
            Cloud = CloudInstance.Gcc,
        });
        var catalog = new CapturingCatalog();
        catalog.Add(new PowerPlatformEnvironmentSummary(
            Guid.NewGuid(),
            "Dev",
            new Uri("https://dev.crm.dynamics.com/"),
            "org",
            "dev",
            null,
            EnvironmentType.Developer));
        var sut = new EnvironmentManagementService(
            new ThrowingResolver(), credentials, catalog, new CapturingProvisioner());

        var result = await sut.ListAsync(null, credentialId: null, cloud: null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("auth:admin", catalog.LastConnection!.Id);
        Assert.Null(catalog.LastConnection.EnvironmentUrl);
        Assert.Equal("tenant-id", catalog.LastConnection.TenantId);
        Assert.Equal(CloudInstance.Gcc, catalog.LastConnection.Cloud);
        Assert.Equal("admin", catalog.LastCredential!.Id);
    }

    [Fact]
    public async Task List_WithAuthAndCloud_UsesSelectedCredentialDirectly()
    {
        var credentials = new InMemoryCredentialStore(
            new Credential { Id = "other", Kind = CredentialKind.DeviceCode, Cloud = CloudInstance.Public },
            new Credential { Id = "ops", Kind = CredentialKind.DeviceCode, TenantId = "tenant-id" });
        var catalog = new CapturingCatalog();
        var sut = new EnvironmentManagementService(
            new ThrowingResolver(), credentials, catalog, new CapturingProvisioner());

        await sut.ListAsync(null, "ops", CloudInstance.GccHigh, CancellationToken.None);

        Assert.Equal("ops", catalog.LastCredential!.Id);
        Assert.Equal(CloudInstance.GccHigh, catalog.LastConnection!.Cloud);
        Assert.Equal("tenant-id", catalog.LastConnection.TenantId);
    }

    [Fact]
    public async Task List_WhenNoProfileAndMultipleCredentials_RequiresAuthSelection()
    {
        var credentials = new InMemoryCredentialStore(
            new Credential { Id = "a", Kind = CredentialKind.DeviceCode },
            new Credential { Id = "b", Kind = CredentialKind.InteractiveBrowser });
        var sut = new EnvironmentManagementService(
            new ThrowingResolver(), credentials, new CapturingCatalog(), new CapturingProvisioner());

        var ex = await Assert.ThrowsAsync<ConfigurationResolutionException>(
            () => sut.ListAsync(null, credentialId: null, cloud: null, CancellationToken.None));

        Assert.Contains("--auth", ex.Message);
    }

    [Fact]
    public async Task Create_FallsBackToSingleAuthCredential_WhenNoProfileResolves()
    {
        var credentials = new InMemoryCredentialStore(new Credential
        {
            Id = "admin",
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "tenant-id",
        });
        var provisioner = new CapturingProvisioner();
        var sut = new EnvironmentManagementService(
            new ThrowingResolver(), credentials, new CapturingCatalog(), provisioner);

        var result = await sut.CreateAsync(null, new EnvironmentCreateOptions
        {
            DisplayName = "First",
            EnvironmentType = EnvironmentType.Developer,
            Cloud = CloudInstance.Public,
        }, CancellationToken.None);

        Assert.Equal("Queued", result.Status);
        Assert.Equal("admin", provisioner.LastCredential!.Id);
        Assert.Equal("auth:admin", provisioner.LastConnection!.Id);
        Assert.Null(provisioner.LastConnection.EnvironmentUrl);
        Assert.Equal("tenant-id", provisioner.LastConnection.TenantId);
        Assert.Equal("First", provisioner.LastCreateRequest!.DisplayName);
    }

    private sealed class ThrowingResolver : IConfigurationResolver
    {
        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
            => throw new ConfigurationResolutionException("No txc profile could be resolved.");
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, Credential> _credentials;

        public InMemoryCredentialStore(params Credential[] credentials)
            => _credentials = credentials.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<Credential>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Credential>>(_credentials.Values.ToList());

        public Task<Credential?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult(_credentials.TryGetValue(id, out var credential) ? credential : null);

        public Task UpsertAsync(Credential credential, CancellationToken ct)
        {
            _credentials[credential.Id] = credential;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct)
            => Task.FromResult(_credentials.Remove(id));
    }

    private sealed class CapturingCatalog : IPowerPlatformEnvironmentCatalog
    {
        private readonly List<PowerPlatformEnvironmentSummary> _environments = [];

        public ConnectionModel? LastConnection { get; private set; }
        public Credential? LastCredential { get; private set; }

        public void Add(PowerPlatformEnvironmentSummary environment)
            => _environments.Add(environment);

        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(
            ConnectionModel connection,
            Credential credential,
            CancellationToken ct)
        {
            LastConnection = connection;
            LastCredential = credential;
            return Task.FromResult<IReadOnlyList<PowerPlatformEnvironmentSummary>>(_environments);
        }

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
            ConnectionModel connection,
            Credential credential,
            Uri environmentUrl,
            CancellationToken ct)
            => Task.FromResult<PowerPlatformEnvironmentSummary?>(null);
    }

    private sealed class CapturingProvisioner : IPowerPlatformEnvironmentProvisioner
    {
        public ConnectionModel? LastConnection { get; private set; }
        public Credential? LastCredential { get; private set; }
        public EnvironmentCreateRequest? LastCreateRequest { get; private set; }

        public Task<EnvironmentCreateResult> CreateAsync(
            ConnectionModel connection,
            Credential credential,
            EnvironmentCreateRequest request,
            CancellationToken ct)
        {
            LastConnection = connection;
            LastCredential = credential;
            LastCreateRequest = request;
            return Task.FromResult(new EnvironmentCreateResult(
                Guid.NewGuid(),
                request.DisplayName,
                null,
                request.EnvironmentType,
                "Queued",
                Completed: false,
                OperationLocation: null));
        }

        public Task<EnvironmentUpdateResult> UpdateAsync(
            ConnectionModel connection,
            Credential credential,
            EnvironmentUpdateRequest request,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<EnvironmentDeleteResult> DeleteAsync(
            ConnectionModel connection,
            Credential credential,
            Guid environmentId,
            bool wait,
            TimeSpan maxWait,
            CancellationToken ct)
            => throw new NotSupportedException();
    }
}
