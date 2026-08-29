using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Tests.Governance;

/// <summary>
/// Shared test host for <c>txc governance environment-group</c> command
/// tests. Registers a fixed profile context and an in-memory fake
/// <see cref="IPowerPlatformEnvironmentGroupClient"/> so tests exercise the
/// full CLI command pipeline (argument binding, resolution, output
/// formatting) without any real HTTP calls.
/// </summary>
internal sealed class GovernanceCommandTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public GovernanceCommandTestHost(FakeEnvironmentGroupClient? client = null)
    {
        Client = client ?? new FakeEnvironmentGroupClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfigurationResolver>(new FixedResolver(TestContext()));
        services.AddSingleton<IPowerPlatformEnvironmentGroupClient>(Client);

        _provider = services.BuildServiceProvider();
        TxcServices.Initialize(_provider);
    }

    public FakeEnvironmentGroupClient Client { get; }

    public void Dispose()
    {
        TxcServices.Reset();
        _provider.Dispose();
    }

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

    private sealed class FixedResolver(ResolvedProfileContext context) : IConfigurationResolver
    {
        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct) => Task.FromResult(context);
    }

    /// <summary>
    /// In-memory fake implementing every environment-group operation against
    /// a simple dictionary, so tests can assert on call arguments and seed
    /// pre-existing groups without any HTTP mocking.
    /// </summary>
    internal sealed class FakeEnvironmentGroupClient : IPowerPlatformEnvironmentGroupClient
    {
        private readonly Dictionary<Guid, PowerPlatformEnvironmentGroup> _groups = new();

        public List<(Guid GroupId, Guid EnvironmentId)> AddedEnvironments { get; } = new();
        public List<(Guid GroupId, Guid EnvironmentId)> RemovedEnvironments { get; } = new();
        public List<Guid> Deleted { get; } = new();

        /// <summary>When set, delete calls throw this exception instead of succeeding (used to simulate 409 conflicts).</summary>
        public Exception? DeleteException { get; set; }

        public PowerPlatformEnvironmentGroup Add(string displayName, string? description = null, IReadOnlyList<Guid>? environmentIds = null)
        {
            var group = new PowerPlatformEnvironmentGroup(
                Guid.NewGuid(), displayName, description, DateTimeOffset.UtcNow, null, null, environmentIds ?? Array.Empty<Guid>());
            _groups[group.Id] = group;
            return group;
        }

        public Task<IReadOnlyList<PowerPlatformEnvironmentGroup>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PowerPlatformEnvironmentGroup>>(_groups.Values.ToList());

        public Task<PowerPlatformEnvironmentGroup?> GetAsync(Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct)
            => Task.FromResult(_groups.GetValueOrDefault(environmentGroupId));

        public Task<PowerPlatformEnvironmentGroup> CreateAsync(Connection connection, Credential credential, PowerPlatformEnvironmentGroupCreateOptions options, CancellationToken ct)
        {
            var group = new PowerPlatformEnvironmentGroup(Guid.NewGuid(), options.DisplayName, options.Description, DateTimeOffset.UtcNow, null, null, Array.Empty<Guid>());
            _groups[group.Id] = group;
            return Task.FromResult(group);
        }

        public Task<PowerPlatformEnvironmentGroup> UpdateAsync(Connection connection, Credential credential, Guid environmentGroupId, PowerPlatformEnvironmentGroupUpdateOptions options, CancellationToken ct)
        {
            var existing = _groups[environmentGroupId];
            var updated = existing with
            {
                DisplayName = options.DisplayName ?? existing.DisplayName,
                Description = options.Description ?? existing.Description,
            };
            _groups[environmentGroupId] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteAsync(Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct)
        {
            if (DeleteException is not null)
                throw DeleteException;

            _groups.Remove(environmentGroupId);
            Deleted.Add(environmentGroupId);
            return Task.CompletedTask;
        }

        public Task AddEnvironmentAsync(Connection connection, Credential credential, Guid environmentGroupId, Guid environmentId, CancellationToken ct)
        {
            AddedEnvironments.Add((environmentGroupId, environmentId));
            var existing = _groups[environmentGroupId];
            _groups[environmentGroupId] = existing with { EnvironmentIds = existing.EnvironmentIds.Append(environmentId).ToList() };
            return Task.CompletedTask;
        }

        public Task RemoveEnvironmentAsync(Connection connection, Credential credential, Guid environmentGroupId, Guid environmentId, CancellationToken ct)
        {
            RemovedEnvironments.Add((environmentGroupId, environmentId));
            var existing = _groups[environmentGroupId];
            _groups[environmentGroupId] = existing with { EnvironmentIds = existing.EnvironmentIds.Where(id => id != environmentId).ToList() };
            return Task.CompletedTask;
        }
    }
}
