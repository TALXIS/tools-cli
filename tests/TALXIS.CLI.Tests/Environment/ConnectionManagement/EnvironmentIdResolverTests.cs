using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class EnvironmentIdResolverTests
{
    private static readonly Guid CatalogId = new("11111111-2222-3333-4444-555555555555");

    private sealed class RecordingCatalog(Guid? answer) : IPowerPlatformEnvironmentCatalog
    {
        public int Lookups { get; private set; }

        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
            Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
        {
            Lookups++;
            return Task.FromResult(answer is { } id
                ? new PowerPlatformEnvironmentSummary(id, "Env", environmentUrl, null, null, null)
                : null);
        }
    }

    private static Connection WithoutStoredId()
    {
        var connection = ConnectionFixtures.Connection();
        connection.EnvironmentId = null;
        return connection;
    }

    [Fact]
    public async Task TheStoredIdWins_WithoutAskingTheCatalog()
    {
        var catalog = new RecordingCatalog(CatalogId);

        var id = await new EnvironmentIdResolver(catalog).ResolveAsync(
            ConnectionFixtures.Connection(), ConnectionFixtures.User(), CancellationToken.None);

        Assert.Equal(ConnectionFixtures.EnvironmentId, id);
        Assert.Equal(0, catalog.Lookups);
    }

    [Fact]
    public async Task WithoutAStoredId_TheCatalogIsAskedForTheUrl()
    {
        var id = await new EnvironmentIdResolver(new RecordingCatalog(CatalogId))
            .ResolveAsync(WithoutStoredId(), ConnectionFixtures.User(), CancellationToken.None);

        Assert.Equal(CatalogId, id);
    }

    [Fact]
    public async Task AnUnknownEnvironment_IsAnError()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new EnvironmentIdResolver(new RecordingCatalog(null))
            .ResolveAsync(WithoutStoredId(), ConnectionFixtures.User(), CancellationToken.None));

        Assert.Contains("Could not resolve a Power Platform environment", ex.Message);
    }

    private sealed class FailingCatalog : IPowerPlatformEnvironmentCatalog
    {
        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
            Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
            => throw new InvalidOperationException("Power Platform environment lookup failed (403 Forbidden).");
    }

    [Fact]
    public async Task ADeniedLookupForAnApplicationCredential_SaysHowToStoreTheId()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new EnvironmentIdResolver(new FailingCatalog())
            .ResolveAsync(WithoutStoredId(), ConnectionFixtures.ServicePrincipal(), CancellationToken.None));

        Assert.Contains("403 Forbidden", ex.Message);
        Assert.Contains("store the environment ID", ex.Message);
    }

    [Fact]
    public async Task ADeniedLookupForAUser_IsNotRewritten()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new EnvironmentIdResolver(new FailingCatalog())
            .ResolveAsync(WithoutStoredId(), ConnectionFixtures.User(), CancellationToken.None));

        Assert.DoesNotContain("store the environment ID", ex.Message);
    }

    [Fact]
    public async Task ADataverseUrlThatIsNotAUri_IsAnError()
    {
        var connection = WithoutStoredId();
        connection.EnvironmentUrl = "not a url";

        await Assert.ThrowsAsync<InvalidOperationException>(() => new EnvironmentIdResolver(new RecordingCatalog(CatalogId))
            .ResolveAsync(connection, ConnectionFixtures.User(), CancellationToken.None));
    }
}
